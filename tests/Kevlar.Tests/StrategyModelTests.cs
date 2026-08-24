using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class StrategyModelTests
{
    [Test]
    public Task Circuit_Breaker_Matches_Reference_State_Machine() => ModelRunner.RunAsync(
        "circuit breaker",
        commandCount: 40,
        random => new CircuitCommand(
            (CircuitCommandKind)random.Next(Enum.GetValues<CircuitCommandKind>().Length),
            random.Next(0, 6)),
        ExecuteCircuitModelAsync);

    [Test]
    public Task Rate_Limiter_Matches_Token_Bucket_Model() => ModelRunner.RunAsync(
        "rate limiter",
        commandCount: 50,
        random => new RateCommand(
            random.Next(3) == 0 ? RateCommandKind.Advance : RateCommandKind.Acquire,
            random.Next(0, 6)),
        ExecuteRateModelAsync);

    [Test]
    public Task Concurrency_Limiter_Matches_Admission_Model() => ModelRunner.RunAsync(
        "concurrency limiter",
        commandCount: 35,
        random => new ConcurrencyCommand(
            (ConcurrencyCommandKind)random.Next(Enum.GetValues<ConcurrencyCommandKind>().Length)),
        ExecuteConcurrencyModelAsync);

    [Test]
    public Task Retry_Matches_Attempt_Model_And_Backoff_Domains() => ModelRunner.RunAsync(
        "retry and backoff",
        commandCount: 20,
        random => new RetryCommand(
            (RetryOutcomeKind)random.Next(Enum.GetValues<RetryOutcomeKind>().Length),
            random.Next(0, 6)),
        ExecuteRetryModelAsync);

    [Test]
    public Task Composition_Matches_Order_And_Outcome_Model() => ModelRunner.RunAsync(
        "composition",
        commandCount: 12,
        random => new CompositionCommand(random.Next(1, 1000), random.Next(3) == 0),
        ExecuteCompositionModelAsync);

    [Test]
    public Task Timeout_Matches_Cancellation_Arbitration_Model() => ModelRunner.RunAsync(
        "timeout cancellation arbitration",
        commandCount: 20,
        random => new TimeoutCommand(
            (TimeoutSignal)random.Next(Enum.GetValues<TimeoutSignal>().Length),
            (CancellationShape)random.Next(Enum.GetValues<CancellationShape>().Length)),
        ExecuteTimeoutModelAsync);

    private static async Task ExecuteTimeoutModelAsync(IReadOnlyList<TimeoutCommand> commands)
    {
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.Timeout(TimeSpan.FromSeconds(1)).WithTimeProvider(timeProvider);

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            using var callerCancellation = new CancellationTokenSource();
            using var foreignCancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellation = command.Shape switch
            {
                CancellationShape.ExecutionToken => null,
                CancellationShape.ForeignToken => new OperationCanceledException(
                    "foreign cancellation",
                    foreignCancellation.Token),
                CancellationShape.NoToken => new OperationCanceledException("unattributed cancellation"),
                CancellationShape.TaskCanceled => new TaskCanceledException("wrapped cancellation"),
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };

            var execution = shield.ExecuteOutcomeAsync<int>(async token =>
            {
                started.TrySetResult();
                if (command.Signal != TimeoutSignal.None)
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                throw cancellation ?? new OperationCanceledException(token);
            }, callerCancellation.Token).AsTask();

            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (command.Signal == TimeoutSignal.CallerAndTimeout)
            {
                callerCancellation.Cancel();
            }

            if (command.Signal != TimeoutSignal.None)
            {
                timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            if (command.Signal == TimeoutSignal.Timeout)
            {
                Ensure(outcome.Exception is TimeoutExceededException, index, command, "timeout outcome differs");
                Ensure(
                    outcome.Exception?.InnerException is OperationCanceledException,
                    index,
                    command,
                    "timeout inner exception is missing");
                Ensure(
                    cancellation is null || ReferenceEquals(outcome.Exception?.InnerException, cancellation),
                    index,
                    command,
                    "timeout inner exception identity differs");
            }
            else if (command.Signal == TimeoutSignal.CallerAndTimeout)
            {
                Ensure(
                    outcome.Exception is OperationCanceledException caller
                    && caller.CancellationToken == callerCancellation.Token,
                    index,
                    command,
                    "caller cancellation did not win");
            }
            else
            {
                Ensure(outcome.Exception is OperationCanceledException, index, command, "cancellation outcome differs");
                Ensure(cancellation is null || ReferenceEquals(outcome.Exception, cancellation),
                    index,
                    command,
                    "spontaneous cancellation identity differs");
            }
        }
    }

    private static async Task ExecuteCircuitModelAsync(IReadOnlyList<CircuitCommand> commands)
    {
        var timeProvider = new ModelTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .When<ModelFailureException>()
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 2;
                options.BreakDuration = TimeSpan.FromSeconds(3);
                options.Monitor = monitor;
            })
            .WithTimeProvider(timeProvider);
        var state = CircuitState.Closed;
        var consecutiveFailures = 0;
        var elapsedSeconds = 0;
        var openUntil = 0;

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            switch (command.Kind)
            {
                case CircuitCommandKind.Advance:
                    timeProvider.Advance(TimeSpan.FromSeconds(command.Amount));
                    elapsedSeconds += command.Amount;
                    break;

                case CircuitCommandKind.MoveUtcBackward:
                    if (state == CircuitState.Closed)
                    {
                        timeProvider.MoveUtcBackward(TimeSpan.FromDays(command.Amount + 1));
                    }

                    break;

                case CircuitCommandKind.Isolate:
                    monitor.Isolate();
                    state = CircuitState.Isolated;
                    break;

                case CircuitCommandKind.Reset:
                    monitor.Reset();
                    state = CircuitState.Closed;
                    consecutiveFailures = 0;
                    break;

                case CircuitCommandKind.Succeed:
                case CircuitCommandKind.Fail:
                case CircuitCommandKind.Unhandled:
                    var shouldSucceed = command.Kind == CircuitCommandKind.Succeed;
                    var invoked = false;
                    var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
                    {
                        invoked = true;
                        return shouldSucceed
                            ? new ValueTask<int>(42)
                            : command.Kind == CircuitCommandKind.Fail
                                ? throw new ModelFailureException()
                                : throw new ArgumentException("unhandled");
                    });

                    var expectedInvocation = state == CircuitState.Closed
                        || state == CircuitState.HalfOpen
                        || (state == CircuitState.Open && elapsedSeconds >= openUntil);
                    Ensure(invoked == expectedInvocation, index, command, "delegate admission differs");

                    if (!expectedInvocation)
                    {
                        Ensure(outcome.Exception is CircuitOpenException, index, command, "expected circuit rejection");
                        break;
                    }

                    if (shouldSucceed)
                    {
                        state = CircuitState.Closed;
                        consecutiveFailures = 0;
                        Ensure(outcome.IsSuccess && outcome.Result == 42, index, command, "success outcome differs");
                    }
                    else if (command.Kind == CircuitCommandKind.Fail)
                    {
                        if (state is CircuitState.Open or CircuitState.HalfOpen)
                        {
                            state = CircuitState.Open;
                            openUntil = elapsedSeconds + 3;
                        }
                        else if (++consecutiveFailures >= 2)
                        {
                            state = CircuitState.Open;
                            openUntil = elapsedSeconds + 3;
                        }

                        Ensure(outcome.Exception is ModelFailureException, index, command, "failure outcome differs");
                    }
                    else
                    {
                        if (state == CircuitState.Open)
                        {
                            state = CircuitState.HalfOpen;
                        }

                        Ensure(outcome.Exception is ArgumentException, index, command, "unhandled outcome differs");
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(command));
            }

            Ensure(monitor.State == state, index, command, $"state differs; expected {state}, actual {monitor.State}");
        }
    }

    private static async Task ExecuteRateModelAsync(IReadOnlyList<RateCommand> commands)
    {
        const int permits = 3;
        const double tokensPerSecond = permits / 6d;
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.RateLimit(permits, TimeSpan.FromSeconds(6)).WithTimeProvider(timeProvider);
        var tokens = (double)permits;

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            if (command.Kind == RateCommandKind.Advance)
            {
                timeProvider.Advance(TimeSpan.FromSeconds(command.Amount));
                tokens = Math.Min(permits, tokens + (command.Amount * tokensPerSecond));
                continue;
            }

            var expectedSuccess = tokens >= 1;
            if (expectedSuccess)
            {
                tokens--;
            }

            var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));
            Ensure(outcome.IsSuccess == expectedSuccess, index, command, "token admission differs");
            if (!expectedSuccess)
            {
                Ensure(outcome.Exception is RateLimitExceededException, index, command, "expected rate-limit rejection");
            }
        }

        await VerifyRateQueueCancellationAsync(commands.Count);
    }

    private static async Task VerifyRateQueueCancellationAsync(int discriminator)
    {
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromSeconds(1);
            options.QueueLimit = 1;
        }).WithTimeProvider(timeProvider);
        using var firstCancellation = new CancellationTokenSource();
        using var replacementCancellation = new CancellationTokenSource();

        _ = await shield.ExecuteAsync(_ => new ValueTask<int>(discriminator));
        var queued = shield.ExecuteAsync(_ => new ValueTask<int>(1), firstCancellation.Token).AsTask();
        Ensure(!queued.IsCompleted, 0, "queue", "first excess call must queue");
        var rejected = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(2));
        Ensure(rejected.Exception is RateLimitExceededException, 0, "queue", "queue overflow must reject");

        firstCancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        var replacement = shield.ExecuteAsync(_ => new ValueTask<int>(3), replacementCancellation.Token).AsTask();
        Ensure(!replacement.IsCompleted, 0, "queue", "cancellation must return queue capacity");
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Ensure(await replacement == 3, 0, "queue", "replacement reservation must complete");
    }

    private static async Task ExecuteConcurrencyModelAsync(IReadOnlyList<ConcurrencyCommand> commands)
    {
        const int capacity = 2;
        const int queueLimit = 2;
        var shield = Shield.ConcurrencyLimit(capacity, queueLimit);
        var operations = new List<PendingOperation>();

        try
        {
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                switch (command.Kind)
                {
                    case ConcurrencyCommandKind.Start:
                        var runningBefore = operations.Count(item =>
                            item.Entered.Task.IsCompleted && !item.Execution!.IsCompleted);
                        var queuedBefore = operations.Count(item =>
                            !item.Entered.Task.IsCompleted && !item.Execution!.IsCompleted);
                        var operation = new PendingOperation();
                        operation.Execution = shield.ExecuteOutcomeAsync<int>(operation.RunAsync, operation.Token).AsTask();
                        operations.Add(operation);
                        if (runningBefore < capacity)
                        {
                            await operation.Entered.Task;
                        }
                        else if (queuedBefore < queueLimit)
                        {
                            Ensure(!operation.Entered.Task.IsCompleted && !operation.Execution.IsCompleted,
                                index, command, "admitted call should queue");
                        }
                        else
                        {
                            var outcome = await operation.Execution;
                            Ensure(outcome.Exception is ConcurrencyLimitExceededException,
                                index, command, "overflow call should reject");
                        }

                        break;

                    case ConcurrencyCommandKind.Release:
                        var active = operations.FirstOrDefault(item =>
                            item.Entered.Task.IsCompleted && !item.Execution!.IsCompleted);
                        var next = operations.FirstOrDefault(item =>
                            !item.Entered.Task.IsCompleted && !item.Execution!.IsCompleted);
                        active?.Release();
                        if (active is not null)
                        {
                            _ = await active.Execution!;
                            if (next is not null)
                            {
                                await next.Entered.Task;
                            }
                        }

                        break;

                    case ConcurrencyCommandKind.CancelQueued:
                        var waiting = operations.FirstOrDefault(item =>
                            !item.Entered.Task.IsCompleted && !item.Execution!.IsCompleted);
                        waiting?.Cancel();
                        if (waiting is not null)
                        {
                            var outcome = await waiting.Execution!;
                            Ensure(outcome.Exception is OperationCanceledException, index, command, "queued cancellation differs");
                        }

                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(command));
                }
            }
        }
        finally
        {
            foreach (var operation in operations)
            {
                operation.Cancel();
                operation.Release();
            }

            await Task.WhenAll(operations.Select(operation => operation.Execution!));
            foreach (var operation in operations)
            {
                operation.Dispose();
            }
        }
    }

    private static async Task ExecuteRetryModelAsync(IReadOnlyList<RetryCommand> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        var maxRetries = commands[0].DelaySelector % 6;
        var attempts = 0;
        var delayAttempts = new List<int>();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = maxRetries;
            options.Backoff = Backoff.Custom(attempt =>
            {
                delayAttempts.Add(attempt);
                return TimeSpan.Zero;
            });
        });
        var expectedAttempts = 0;
        var expectedSuccess = false;
        for (; expectedAttempts <= maxRetries; expectedAttempts++)
        {
            var kind = commands[Math.Min(expectedAttempts, commands.Count - 1)].Kind;
            if (kind == RetryOutcomeKind.Success)
            {
                expectedSuccess = true;
                expectedAttempts++;
                break;
            }

            if (kind == RetryOutcomeKind.Rejection)
            {
                expectedAttempts++;
                break;
            }
        }

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            var command = commands[Math.Min(attempts, commands.Count - 1)];
            attempts++;
            return command.Kind switch
            {
                RetryOutcomeKind.Success => new ValueTask<int>(42),
                RetryOutcomeKind.OrdinaryFailure => throw new ModelFailureException(),
                RetryOutcomeKind.Rejection => throw new RateLimitExceededException(retryAfter: null),
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };
        });

        Ensure(attempts == expectedAttempts, 0, "retry", "attempt count differs");
        Ensure(outcome.IsSuccess == expectedSuccess, 0, "retry", "final outcome differs");
        var finalKind = commands[Math.Min(attempts - 1, commands.Count - 1)].Kind;
        if (!expectedSuccess)
        {
            Ensure(
                finalKind == RetryOutcomeKind.Rejection
                    ? outcome.Exception is RateLimitExceededException
                    : outcome.Exception is ModelFailureException,
                0,
                "retry",
                "final exception differs");
        }

        Ensure(delayAttempts.SequenceEqual(Enumerable.Range(1, Math.Max(0, attempts - 1))), 0, "retry", "delay attempt numbers differ");

        foreach (var command in commands)
        {
            var initial = TimeSpan.FromTicks(command.DelaySelector);
            var delay = Backoff.Exponential(initial, factor: 2, maxDelay: TimeSpan.FromTicks(20), jitter: false)
                .GetDelay(command.DelaySelector + 1);
            Ensure(delay >= TimeSpan.Zero && delay <= TimeSpan.FromTicks(20), 0, command, "backoff escaped its domain");
        }
    }

    private static async Task ExecuteCompositionModelAsync(IReadOnlyList<CompositionCommand> commands)
    {
        var log = new List<int>();
        var observers = Shield<int>.Empty;
        foreach (var command in commands)
        {
            observers = observers.Use(new RecordingStrategy(command.Id, log));
        }

        var shouldFail = commands.Count > 0 && commands[^1].Fail;
        var shield = Shield.For<int>()
            .When<ModelFailureException>()
            .FallbackTo(99)
            .Wrap(observers);
        var result = await shield.ExecuteAsync(_ => shouldFail
            ? throw new ModelFailureException()
            : new ValueTask<int>(42));

        Ensure(log.SequenceEqual(commands.Select(command => command.Id)), 0, "composition", "strategy order differs");
        Ensure(result == (shouldFail ? 99 : 42), 0, "composition", "handled outcome propagation differs");
    }

    private static void Ensure(bool condition, int index, object command, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Command {index} ({command}): {message}.");
        }
    }

    private enum CircuitCommandKind
    {
        Succeed,
        Fail,
        Unhandled,
        Advance,
        MoveUtcBackward,
        Isolate,
        Reset,
    }

    private readonly record struct CircuitCommand(CircuitCommandKind Kind, int Amount);

    private enum RateCommandKind
    {
        Acquire,
        Advance,
    }

    private readonly record struct RateCommand(RateCommandKind Kind, int Amount);

    private enum ConcurrencyCommandKind
    {
        Start,
        Release,
        CancelQueued,
    }

    private readonly record struct ConcurrencyCommand(ConcurrencyCommandKind Kind);

    private enum RetryOutcomeKind
    {
        Success,
        OrdinaryFailure,
        Rejection,
    }

    private readonly record struct RetryCommand(RetryOutcomeKind Kind, int DelaySelector);

    private readonly record struct CompositionCommand(int Id, bool Fail);

    private enum TimeoutSignal
    {
        None,
        Timeout,
        CallerAndTimeout,
    }

    private enum CancellationShape
    {
        ExecutionToken,
        ForeignToken,
        NoToken,
        TaskCanceled,
    }

    private readonly record struct TimeoutCommand(TimeoutSignal Signal, CancellationShape Shape);

    private sealed class ModelFailureException : Exception;

    private sealed class RecordingStrategy(int id, List<int> log) : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            log.Add(id);
            return next.InvokeAsync(context);
        }
    }

    private sealed class PendingOperation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Outcome<int>>? Execution { get; set; }

        public bool Released => _release.Task.IsCompleted;

        public async ValueTask<int> RunAsync(CancellationToken token)
        {
            Entered.TrySetResult();
            await _release.Task.WaitAsync(token);
            return 42;
        }

        public void Cancel() => _cancellation.Cancel();

        public void Release() => _release.TrySetResult();

        public CancellationToken Token => _cancellation.Token;

        public void Dispose() => _cancellation.Dispose();
    }

    private sealed class ModelTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed)
        {
            _timestamp += elapsed.Ticks;
            _utcNow += elapsed;
        }

        public void MoveUtcBackward(TimeSpan elapsed) => _utcNow -= elapsed;
    }
}

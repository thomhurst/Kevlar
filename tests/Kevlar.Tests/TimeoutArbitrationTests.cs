namespace Kevlar.Tests;

public class TimeoutArbitrationTests
{
    [Test]
    public async Task Caller_Cancellation_Wins_After_Timeout_Fires()
    {
        var timeProvider = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = 0;
        CancellationToken executionToken = default;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            executionToken = token;
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException("wrapped without token");
        }, callerCancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        callerCancellation.Cancel();
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(executionToken).IsNotEqualTo(callerCancellation.Token);
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken == callerCancellation.Token)
            .IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Caller_Cancellation_Wins_Before_Timeout_Fires()
    {
        var timeProvider = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = 0;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }, callerCancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callerCancellation.Cancel();
        timeProvider.Fire(0);
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken == callerCancellation.Token)
            .IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Equal_Nested_Timeouts_Report_Only_The_Outer_Timeout()
    {
        var timeProvider = new ManualTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outerEvents = 0;
        var innerEvents = 0;
        var shield = Shield
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ => outerEvents++;
            })
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ => innerEvents++;
            })
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(1);
        timeProvider.Fire(0);
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(outerEvents).IsEqualTo(1);
        await Assert.That(innerEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Timeout_Converts_OperationCanceledException_With_Foreign_Token()
    {
        using var unrelatedCancellation = new CancellationTokenSource();
        var unrelated = new OperationCanceledException("unrelated", unrelatedCancellation.Token);

        await AssertCancellationIsTimeoutAsync(unrelated);
    }

    [Test]
    public async Task Timeout_Converts_OperationCanceledException_Without_Token() =>
        await AssertCancellationIsTimeoutAsync(new OperationCanceledException("cancelled"));

    [Test]
    public async Task Timeout_Converts_TaskCanceledException_Without_Token() =>
        await AssertCancellationIsTimeoutAsync(new TaskCanceledException("cancelled"));

    [Test]
    public async Task Dynamic_Timeout_Converts_OperationCanceledException_Without_Token() =>
        await AssertCancellationIsTimeoutAsync(
            new OperationCanceledException("cancelled"),
            useTimeoutGenerator: true);

    [Test]
    public async Task Async_Timeout_Notification_Preserves_Original_Cancellation()
    {
        var timeProvider = new ManualTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new OperationCanceledException("wrapped without token");
        var timeoutEvents = 0;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeoutAsync = async _ =>
            {
                await Task.Yield();
                timeoutEvents++;
            };
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async _ =>
        {
            started.SetResult();
            await release.Task;
            throw cancellation;
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        var timeout = outcome.Exception as TimeoutExceededException;
        await Assert.That(timeout).IsNotNull();
        await Assert.That(ReferenceEquals(timeout!.InnerException, cancellation)).IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(1);
    }

    [Test]
    public async Task OperationCanceledException_Before_Timeout_Fires_Is_Preserved()
    {
        var timeProvider = new ManualTimeProvider();
        var cancellation = new OperationCanceledException("spontaneous");
        var timeoutEvents = 0;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ => throw cancellation);

        await Assert.That(ReferenceEquals(outcome.Exception, cancellation)).IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Timeout_Then_Retry_Retries_OperationCanceledException_Without_Token()
    {
        var timeProvider = new ManualTimeProvider();
        var attempts = new AsyncCounter("foreign-token timeout attempts");
        var shield = Shield
            .When<TimeoutExceededException>()
            .Retry(1, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(token =>
            CancelWithoutTokenAsync(token, attempts)).AsTask();

        await attempts.WaitForAsync(1);
        timeProvider.Fire(0);
        await attempts.WaitForAsync(2);
        timeProvider.Fire(1);
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(attempts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Timeout_Then_CircuitBreaker_Counts_OperationCanceledException_Without_Token()
    {
        var timeProvider = new ManualTimeProvider();
        var attempts = new AsyncCounter("foreign-token breaker attempts");
        var shield = Shield
            .When<TimeoutExceededException>()
            .CircuitBreaker(consecutiveFailures: 1, breakDuration: TimeSpan.FromMinutes(1))
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(timeProvider);

        var first = shield.ExecuteOutcomeAsync<int>(token =>
            CancelWithoutTokenAsync(token, attempts)).AsTask();

        await attempts.WaitForAsync(1);
        timeProvider.Fire(0);
        var firstOutcome = await first.WaitAsync(TimeSpan.FromSeconds(5));
        var rejected = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));

        await Assert.That(firstOutcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(rejected.Exception).IsTypeOf<CircuitOpenException>();
    }

    [Test]
    public async Task Per_Attempt_Timeout_Retries_Then_Outer_Timeout_Wins()
    {
        var timeProvider = new ManualTimeProvider();
        var attempts = new AsyncCounter("nested timeout attempts");
        var shield = Shield
            .Timeout(TimeSpan.FromSeconds(30))
            .When<TimeoutExceededException>()
            .Retry(3, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(5))
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(token =>
            CancelWithoutTokenAsync(token, attempts)).AsTask();

        await attempts.WaitForAsync(1);
        timeProvider.Fire(1);
        await attempts.WaitForAsync(2);
        timeProvider.Fire(0);
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        var timeout = outcome.Exception as TimeoutExceededException;
        await Assert.That(timeout).IsNotNull();
        await Assert.That(timeout!.Timeout).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(timeout.InnerException).IsTypeOf<OperationCanceledException>();
        await Assert.That(attempts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Success_After_Timeout_Token_Cancellation_Is_Delivered()
    {
        var timeProvider = new ManualTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = 0;
        CancellationToken executionToken = default;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(async token =>
        {
            executionToken = token;
            started.SetResult();
            await release.Task;
            return 42;
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        release.SetResult();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(executionToken.IsCancellationRequested).IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Outer_Strategy_Sees_Caller_Token_After_Success_And_Timeout()
    {
        var timeProvider = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var observer = new TokenObserverStrategy();
        var shield = Shield.Use(observer)
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(timeProvider);

        var success = await shield.ExecuteAsync(_ => new ValueTask<int>(42), callerCancellation.Token);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedExecution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }, callerCancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(1);
        release.SetResult();
        var timedOutcome = await timedExecution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(success).IsEqualTo(42);
        await Assert.That(timedOutcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(observer.Observations.Count).IsEqualTo(2);
        foreach (var observation in observer.Observations)
        {
            await Assert.That(observation.Before).IsEqualTo(callerCancellation.Token);
            await Assert.That(observation.After).IsEqualTo(callerCancellation.Token);
        }
    }

    [Test]
    public async Task OnTimeout_Failure_Leaves_Context_Clean_And_Next_Execution_Healthy()
    {
        var timeProvider = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var callbackFailure = new ApplicationException("timeout callback failed");
        var callbackCalls = 0;
        var observer = new TokenObserverStrategy();
        var shield = Shield.Use(observer)
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ =>
                {
                    callbackCalls++;
                    throw callbackFailure;
                };
            })
            .WithTimeProvider(timeProvider);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstExecution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }, callerCancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        release.SetResult();
        var failed = await firstExecution.WaitAsync(TimeSpan.FromSeconds(5));
        var recovered = await shield.ExecuteAsync(_ => new ValueTask<int>(42), callerCancellation.Token);

        await Assert.That(ReferenceEquals(failed.Exception, callbackFailure)).IsTrue();
        await Assert.That(recovered).IsEqualTo(42);
        await Assert.That(callbackCalls).IsEqualTo(1);
        await Assert.That(observer.Observations.Count).IsEqualTo(2);
        foreach (var observation in observer.Observations)
        {
            await Assert.That(observation.Before).IsEqualTo(callerCancellation.Token);
            await Assert.That(observation.After).IsEqualTo(callerCancellation.Token);
        }
    }

    [Test]
    public async Task Queued_Stale_Timer_Cannot_Cancel_A_Later_Execution()
    {
        var timeProvider = new ManualTimeProvider();
        var shield = Shield.Timeout(TimeSpan.FromSeconds(1)).WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken secondToken = default;
        var secondExecution = shield.ExecuteAsync(async token =>
        {
            secondToken = token;
            started.SetResult();
            await release.Task;
            token.ThrowIfCancellationRequested();
            return 2;
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        await Assert.That(secondToken.IsCancellationRequested).IsFalse();
        release.SetResult();

        await Assert.That(await secondExecution.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(2);
    }

    private sealed class TokenObserverStrategy : Strategy
    {
        public List<TokenObservation> Observations { get; } = [];

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var before = context.CancellationToken;
            try
            {
                return await next.InvokeAsync(context);
            }
            finally
            {
                Observations.Add(new TokenObservation(before, context.CancellationToken));
            }
        }
    }

    private static async Task AssertCancellationIsTimeoutAsync(
        OperationCanceledException cancellationException,
        bool useTimeoutGenerator = false)
    {
        var timeProvider = new ManualTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = 0;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            if (useTimeoutGenerator)
            {
                options.TimeoutGenerator = static _ =>
                    new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
            }

            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async _ =>
        {
            started.SetResult();
            await release.Task;
            throw cancellationException;
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        var timeout = outcome.Exception as TimeoutExceededException;
        await Assert.That(timeout).IsNotNull();
        await Assert.That(ReferenceEquals(timeout!.InnerException, cancellationException)).IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(1);
    }

    private static async ValueTask<int> CancelWithoutTokenAsync(
        CancellationToken cancellationToken,
        AsyncCounter attempts)
    {
        attempts.Signal();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(static state =>
            ((TaskCompletionSource)state!).TrySetResult(), cancelled);
        await cancelled.Task;
        throw new OperationCanceledException("wrapped without token");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }

        public void Fire(int timerIndex) => _timers[timerIndex].Fire();

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => default;

            public void Fire() => callback(state);
        }
    }

    private sealed record TokenObservation(CancellationToken Before, CancellationToken After);
}

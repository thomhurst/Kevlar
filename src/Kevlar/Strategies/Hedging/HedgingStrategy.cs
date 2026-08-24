using Kevlar.Internal;
using Reservoir;

namespace Kevlar.Strategies;

internal sealed class HedgingStrategy : Strategy
{
    private readonly OutcomeJudge _judge;
    private readonly int _maxAttempts;
    private readonly TimeSpan _delay;
    private readonly Action<HedgeEvent>? _onHedge;
    private readonly Func<HedgeEvent, ValueTask>? _onHedgeAsync;
    private readonly HedgeActionGenerator? _actionGenerator;

    public HedgingStrategy(HedgeOptions options, OutcomeJudge judge)
        : this(options, judge, options.HasHandlingOverride)
    {
    }

    private HedgingStrategy(HedgeOptions options, OutcomeJudge judge, bool hasHandlingOverride)
    {
        Throw.IfOutOfRange(options.MaxAttempts < 1, nameof(options), "MaxAttempts must be at least 1.");
        Throw.IfOutOfRange(options.Delay < TimeSpan.Zero && options.Delay != System.Threading.Timeout.InfiniteTimeSpan, nameof(options), "Delay must be non-negative or Timeout.InfiniteTimeSpan.");
        Throw.IfOutOfRange(options.Delay > DelayHelper.MaximumDelay, nameof(options.Delay), "Delay exceeds the runtime timer limit.");

        _judge = judge;
        _maxAttempts = options.MaxAttempts;
        _delay = options.Delay;
        _onHedge = options.OnHedge;
        _onHedgeAsync = options.OnHedgeAsync;
        _actionGenerator = options.ActionGenerator;
        HasHandlingOverride = hasHandlingOverride;
    }

    internal static HedgingStrategy Create<TResult>(HedgeOptions<TResult> options, OutcomeJudge judge) =>
        new(options.ToUntyped(), judge, options.HasHandlingOverride);

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    internal int MaxAttempts => _maxAttempts;

    internal TimeSpan Delay => _delay;

    internal bool HasNotification => _onHedge is not null;

    protected internal override bool InvokesContinuationAtMostOnce => _maxAttempts == 1;

    public override string Describe() => $"Hedge({_maxAttempts} attempts, delay {DescribeHelper.Time(_delay)})";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (_maxAttempts == 1)
        {
            return next.InvokeAsync(context);
        }

        if (context.IsSynchronous)
        {
            throw new NotSupportedException("Hedging requires asynchronous execution. Use ExecuteAsync instead of Execute.");
        }

        var primary = StartPrimaryAttempt(next, context);
        if (_delay == TimeSpan.Zero || !primary.Execution.IsCompletedSuccessfully)
        {
            return ExecuteCoreAsync(next, context, primary.AsPending(), launched: 1, default);
        }

        Outcome<T> outcome;
        try
        {
            outcome = primary.Execution.Result;
        }
        finally
        {
            primary.Dispose();
        }

        if (!_judge.ShouldHandle(in outcome))
        {
            return new ValueTask<Outcome<T>>(NormalizeCancellation(outcome, context));
        }

        return ExecuteCoreAsync(next, context, initial: null, launched: 1, outcome);
    }

    private async ValueTask<Outcome<T>> ExecuteCoreAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        HedgeAttempt<T>? initial,
        int launched,
        Outcome<T> lastOutcome)
    {
        var pending = ListPool<HedgeAttempt<T>>.Shared.Rent();

        try
        {
            if (initial is { } primary)
            {
                pending.Add(primary);
            }
            else
            {
                pending.Add(await StartHedgeAttemptAsync(next, context, launched + 1).ConfigureAwait(false));
                launched++;
            }

            while (true)
            {
                if (launched < _maxAttempts && _delay == TimeSpan.Zero)
                {
                    pending.Add(await StartHedgeAttemptAsync(next, context, launched + 1).ConfigureAwait(false));
                    launched++;
                    continue;
                }

                Task<Outcome<T>> completed;

                if (launched < _maxAttempts && _delay != System.Threading.Timeout.InfiniteTimeSpan)
                {
                    using var delayCancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
                    var delayTask = DelayHelper.CreateDelayTask(context.TimeProvider, _delay, delayCancellation.Token);
                    var winner = await WhenAnyAttemptOr(pending, delayTask).ConfigureAwait(false);

                    if (winner == delayTask)
                    {
                        pending.Add(await StartHedgeAttemptAsync(next, context, launched + 1).ConfigureAwait(false));
                        launched++;
                        continue;
                    }

                    delayCancellation.Cancel();
                    completed = (Task<Outcome<T>>)winner;
                }
                else
                {
                    // A single pending attempt needs no WhenAny machinery; awaiting it directly
                    // is equivalent and skips the Task[] allocation.
                    completed = pending.Count == 1
                        ? pending[0].Task
                        : await WhenAnyAttempt(pending).ConfigureAwait(false);
                }

                var outcome = NormalizeCancellation(await completed.ConfigureAwait(false), context);
                Remove(pending, completed);

                if (!_judge.ShouldHandle(in outcome))
                {
                    return outcome;
                }

                lastOutcome = outcome;

                if (launched < _maxAttempts)
                {
                    pending.Add(await StartHedgeAttemptAsync(next, context, launched + 1).ConfigureAwait(false));
                    launched++;
                }
                else if (pending.Count == 0)
                {
                    return lastOutcome;
                }
            }
        }
        finally
        {
            foreach (var attempt in pending)
            {
                attempt.Cancellation.Cancel();
                Cleanup(attempt);
            }

            ListPool<HedgeAttempt<T>>.Shared.Return(pending);
        }
    }

    private ValueTask<HedgeAttempt<T>> StartHedgeAttemptAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var hedgeEvent = new HedgeEvent(attemptNumber, context);
        _onHedge?.Invoke(hedgeEvent);
        context.CancellationToken.ThrowIfCancellationRequested();

        if (_onHedgeAsync is { } onHedgeAsync)
        {
            var notification = onHedgeAsync(hedgeEvent);
            // Stryker disable once all: Route selection is performance-only; both branches await the hook.
            if (!notification.IsCompletedSuccessfully)
            {
                return AwaitHedgeNotificationAsync(notification, next, context, attemptNumber);
            }

            notification.GetAwaiter().GetResult();
            context.CancellationToken.ThrowIfCancellationRequested();
        }

        return new ValueTask<HedgeAttempt<T>>(
            StartHedgeAttempt(next, context, attemptNumber).AsPending());
    }

    private async ValueTask<HedgeAttempt<T>> AwaitHedgeNotificationAsync<T, TState>(
        ValueTask notification,
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber)
    {
        // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
        await notification.ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();
        return StartHedgeAttempt(next, context, attemptNumber).AsPending();
    }

    private StartedAttempt<T> StartPrimaryAttempt<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var cancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        try
        {
            return new StartedAttempt<T>(next.InvokeAsync(fork), cancellation, fork);
        }
        catch
        {
            KevlarContext.Return(fork);
            cancellation.Dispose();
            throw;
        }
    }

    private StartedAttempt<T> StartHedgeAttempt<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber)
    {
        var cancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        try
        {
            Func<CancellationToken, ValueTask<T>>? generatedAction = null;
            if (_actionGenerator is not null)
            {
                Func<CancellationToken, ValueTask<T>> originalAction =
                    token => InvokeOriginalAction(next, fork, token);
                generatedAction = _actionGenerator.Generate(
                    attemptNumber,
                    fork,
                    originalAction);
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            KevlarMetrics.Hedge(context.ShieldName);
            var execution = generatedAction is null
                ? next.InvokeAsync(fork)
                : InvokeGeneratedAction(generatedAction, fork.CancellationToken);
            return new StartedAttempt<T>(execution, cancellation, fork);
        }
        catch
        {
            KevlarContext.Return(fork);
            cancellation.Dispose();
            throw;
        }
    }

    private static ValueTask<T> GetOriginalResultAsync<T>(ValueTask<Outcome<T>> execution)
    {
        // Stryker disable once all: Route selection is performance-only; both branches unwrap the outcome.
        if (execution.IsCompletedSuccessfully)
        {
            return new ValueTask<T>(execution.Result.GetResultOrRethrowInternal());
        }

        return AwaitOriginalResultAsync(execution);
    }

    private static ValueTask<T> InvokeOriginalAction<T, TState>(
        Continuation<T, TState> next,
        KevlarContext attemptContext,
        CancellationToken cancellationToken)
    {
        var invocationContext = attemptContext.Fork(cancellationToken);
        ValueTask<Outcome<T>> execution;
        try
        {
            execution = next.InvokeAsync(invocationContext);
        }
        catch
        {
            KevlarContext.Return(invocationContext);
            throw;
        }

        if (!execution.IsCompletedSuccessfully)
        {
            return AwaitOriginalResultAsync(execution, invocationContext);
        }

        try
        {
            return new ValueTask<T>(execution.Result.GetResultOrRethrowInternal());
        }
        finally
        {
            KevlarContext.Return(invocationContext);
        }
    }

    private static async ValueTask<T> AwaitOriginalResultAsync<T>(ValueTask<Outcome<T>> execution)
    {
        // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
        return (await execution.ConfigureAwait(false)).GetResultOrRethrowInternal();
    }

    private static async ValueTask<T> AwaitOriginalResultAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext invocationContext)
    {
        try
        {
            // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
            return (await execution.ConfigureAwait(false)).GetResultOrRethrowInternal();
        }
        finally
        {
            KevlarContext.Return(invocationContext);
        }
    }

    private static ValueTask<Outcome<T>> InvokeGeneratedAction<T>(
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        ValueTask<T> execution;
        try
        {
            execution = action(cancellationToken);
        }
        catch (Exception exception)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
        }

        // Stryker disable once all: Route selection is performance-only; both branches preserve the outcome.
        if (execution.IsCompletedSuccessfully)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromResult(execution.Result));
        }

        return AwaitGeneratedActionAsync(execution);
    }

    private static async ValueTask<Outcome<T>> AwaitGeneratedActionAsync<T>(ValueTask<T> execution)
    {
        try
        {
            // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
            return Outcome<T>.FromResult(await execution.ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }
    }

    private static Task<Task> WhenAnyAttemptOr<T>(List<HedgeAttempt<T>> pending, Task delayTask)
    {
        if (pending.Count == 1)
        {
            // Common case (one in-flight attempt racing the hedge delay): the two-task
            // overload avoids the Task[] allocation.
            return Task.WhenAny(pending[0].Task, delayTask);
        }

        var tasks = new Task[pending.Count + 1];
        for (var i = 0; i < pending.Count; i++)
        {
            tasks[i] = pending[i].Task;
        }

        tasks[pending.Count] = delayTask;
        return Task.WhenAny(tasks);
    }

    private static Task<Task<Outcome<T>>> WhenAnyAttempt<T>(List<HedgeAttempt<T>> pending)
    {
        var tasks = new Task<Outcome<T>>[pending.Count];
        for (var i = 0; i < pending.Count; i++)
        {
            tasks[i] = pending[i].Task;
        }

        return Task.WhenAny(tasks);
    }

    private static void Remove<T>(List<HedgeAttempt<T>> pending, Task<Outcome<T>> task)
    {
        for (var i = 0; i < pending.Count; i++)
        {
            if (ReferenceEquals(pending[i].Task, task))
            {
                pending[i].Dispose();
                pending.RemoveAt(i);
                return;
            }
        }
    }

    private static void Cleanup<T>(HedgeAttempt<T> attempt)
    {
        if (attempt.Task.IsCompleted)
        {
            attempt.Dispose();
            return;
        }

        _ = attempt.Task.ContinueWith(
            static (_, state) => ((HedgeAttempt<T>)state!).Dispose(),
            attempt,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static Outcome<T> NormalizeCancellation<T>(Outcome<T> outcome, KevlarContext context)
    {
        if (!context.CancellationToken.IsCancellationRequested
            || outcome.Exception is not OperationCanceledException cancellation
            || cancellation.CancellationToken == context.CancellationToken)
        {
            return outcome;
        }

        return Outcome<T>.FromException(new OperationCanceledException(
            cancellation.Message,
            cancellation,
            context.CancellationToken));
    }

    private readonly struct HedgeAttempt<T>
    {
        public HedgeAttempt(Task<Outcome<T>> task, CancellationTokenSource cancellation, KevlarContext context)
        {
            Task = task;
            Cancellation = cancellation;
            Context = context;
        }

        public Task<Outcome<T>> Task { get; }

        public CancellationTokenSource Cancellation { get; }

        public KevlarContext Context { get; }

        public void Dispose()
        {
            KevlarContext.Return(Context);
            Cancellation.Dispose();
        }
    }

    private readonly struct StartedAttempt<T>
    {
        public StartedAttempt(ValueTask<Outcome<T>> execution, CancellationTokenSource cancellation, KevlarContext context)
        {
            Execution = execution;
            Cancellation = cancellation;
            Context = context;
        }

        public ValueTask<Outcome<T>> Execution { get; }

        private CancellationTokenSource Cancellation { get; }

        private KevlarContext Context { get; }

        public HedgeAttempt<T> AsPending() => new(Execution.AsTask(), Cancellation, Context);

        public void Dispose()
        {
            KevlarContext.Return(Context);
            Cancellation.Dispose();
        }
    }
}

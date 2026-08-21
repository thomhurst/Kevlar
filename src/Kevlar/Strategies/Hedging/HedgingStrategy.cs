using Kevlar.Internal;
using Reservoir;

namespace Kevlar.Strategies;

internal sealed class HedgingStrategy : Strategy
{
    private readonly OutcomeJudge _judge;
    private readonly int _maxAttempts;
    private readonly TimeSpan _delay;
    private readonly Action<HedgeEvent>? _onHedge;

    public HedgingStrategy(HedgingOptions options, OutcomeJudge judge)
    {
        Throw.IfOutOfRange(options.MaxAttempts < 1, nameof(options), "MaxAttempts must be at least 1.");
        Throw.IfOutOfRange(options.Delay < TimeSpan.Zero && options.Delay != System.Threading.Timeout.InfiniteTimeSpan, nameof(options), "Delay must be non-negative or Timeout.InfiniteTimeSpan.");

        _judge = judge;
        _maxAttempts = options.MaxAttempts;
        _delay = options.Delay;
        _onHedge = options.OnHedge;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

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

        var primary = StartAttempt(next, context, attemptNumber: 1);
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
            return new ValueTask<Outcome<T>>(outcome);
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
                Launch(pending, next, context, ref launched);
            }

            while (true)
            {
                if (launched < _maxAttempts && _delay == TimeSpan.Zero)
                {
                    Launch(pending, next, context, ref launched);
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
                        Launch(pending, next, context, ref launched);
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

                var outcome = await completed.ConfigureAwait(false);
                Remove(pending, completed);

                if (!_judge.ShouldHandle(in outcome))
                {
                    return outcome;
                }

                lastOutcome = outcome;

                if (launched < _maxAttempts)
                {
                    Launch(pending, next, context, ref launched);
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

    private void Launch<T, TState>(List<HedgeAttempt<T>> pending, Continuation<T, TState> next, KevlarContext context, ref int launched)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var attemptNumber = ++launched;
        pending.Add(StartAttempt(next, context, attemptNumber).AsPending());
    }

    private StartedAttempt<T> StartAttempt<T, TState>(Continuation<T, TState> next, KevlarContext context, int attemptNumber)
    {
        if (attemptNumber > 1)
        {
            _onHedge?.Invoke(new HedgeEvent(attemptNumber, context));
            context.CancellationToken.ThrowIfCancellationRequested();
            KevlarMetrics.Hedge(context.ShieldName);
        }

        var cancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        return new StartedAttempt<T>(next.InvokeAsync(fork), cancellation, fork);
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

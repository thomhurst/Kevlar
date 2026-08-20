using Kevlar.Internal;

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

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (_maxAttempts == 1)
        {
            return await next.InvokeAsync(context).ConfigureAwait(false);
        }

        if (context.IsSynchronous)
        {
            throw new NotSupportedException("Hedging requires asynchronous execution. Use ExecuteAsync instead of Execute.");
        }

        var pending = new List<HedgeAttempt<T>>(_maxAttempts);
        var launched = 0;
        var lastOutcome = default(Outcome<T>);

        try
        {
            Launch(pending, next, context, ref launched);

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
                    using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
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
                    completed = await WhenAnyAttempt(pending).ConfigureAwait(false);
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
        }
    }

    private void Launch<T, TState>(List<HedgeAttempt<T>> pending, Continuation<T, TState> next, KevlarContext context, ref int launched)
    {
        var attemptNumber = ++launched;

        if (attemptNumber > 1)
        {
            KevlarMetrics.Hedge(context.ShieldName);
            _onHedge?.Invoke(new HedgeEvent(attemptNumber, context));
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        pending.Add(new HedgeAttempt<T>(next.InvokeAsync(fork).AsTask(), cancellation));
    }

    private static Task<Task> WhenAnyAttemptOr<T>(List<HedgeAttempt<T>> pending, Task delayTask)
    {
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
                pending[i].Cancellation.Dispose();
                pending.RemoveAt(i);
                return;
            }
        }
    }

    private static void Cleanup<T>(HedgeAttempt<T> attempt)
    {
        if (attempt.Task.IsCompleted)
        {
            attempt.Cancellation.Dispose();
            return;
        }

        _ = attempt.Task.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            attempt.Cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private readonly struct HedgeAttempt<T>
    {
        public HedgeAttempt(Task<Outcome<T>> task, CancellationTokenSource cancellation)
        {
            Task = task;
            Cancellation = cancellation;
        }

        public Task<Outcome<T>> Task { get; }

        public CancellationTokenSource Cancellation { get; }
    }
}

namespace Kevlar.Internal;

internal sealed class ExecutionTrackingStrategy(StrategyExecutionTracker tracker)
    : Strategy, ITransparentStrategy
{
    protected internal override bool InvokesContinuationAtMostOnce => true;

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        tracker.Enter();
        var execution = next.InvokeAsync(context);

        if (execution.IsCompletedSuccessfully)
        {
            tracker.Exit();
            return execution;
        }

        return CompleteAsync(execution);
    }

    private async ValueTask<Outcome<T>> CompleteAsync<T>(ValueTask<Outcome<T>> execution)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            tracker.Exit();
        }
    }
}

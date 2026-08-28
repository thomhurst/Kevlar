namespace Kevlar.Internal;

internal sealed class ExecutionTrackingStrategy(
    StrategyExecutionTracker tracker,
    ExecutionReentrancyGuard reentrancyGuard)
    : Strategy, ITransparentStrategy
{
    protected internal override bool InvokesContinuationAtMostOnce => true;

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var scope = reentrancyGuard.Enter();
        tracker.Enter();
        var execution = next.InvokeAsync(context);

        if (execution.IsCompletedSuccessfully)
        {
            Complete(scope);
            return execution;
        }

        return CompleteAsync(execution, scope);
    }

    private async ValueTask<Outcome<T>> CompleteAsync<T>(
        ValueTask<Outcome<T>> execution,
        ExecutionReentrancyGuard.Scope scope)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            Complete(scope);
        }
    }

    private void Complete(ExecutionReentrancyGuard.Scope scope)
    {
        tracker.Exit();
        reentrancyGuard.Exit(scope);
    }
}

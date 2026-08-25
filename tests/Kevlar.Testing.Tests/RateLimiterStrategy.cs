namespace Kevlar.Extensions.RateLimiting;

internal sealed class RateLimiterStrategy(HandlingClause handling) : Strategy
{
    protected override HandlingClause? Handling => handling;

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context) => next.InvokeAsync(context);
}

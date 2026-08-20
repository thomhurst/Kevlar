using System.Diagnostics;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class FallbackStrategy<TResult> : Strategy
{
    private readonly Func<Outcome<TResult>, KevlarContext, ValueTask<TResult>> _fallback;
    private readonly OutcomeJudge _judge;
    private readonly Action<FallbackEvent>? _onFallback;

    public FallbackStrategy(
        Func<Outcome<TResult>, KevlarContext, ValueTask<TResult>> fallback,
        OutcomeJudge judge,
        Action<FallbackEvent>? onFallback)
    {
        _fallback = fallback;
        _judge = judge;
        _onFallback = onFallback;
    }

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var outcome = await next.InvokeAsync(context).ConfigureAwait(false);

        if (!_judge.ShouldHandle(in outcome))
        {
            return outcome;
        }

        _onFallback?.Invoke(new FallbackEvent(
            outcome.Exception,
            outcome.Exception is null ? (object?)outcome.Result : null,
            context));

        Debug.Assert(typeof(T) == typeof(TResult), "Fallback strategies only execute inside a matching Policy<TResult>.");
        var fallback = (Func<Outcome<T>, KevlarContext, ValueTask<T>>)(object)_fallback;

        try
        {
            return Outcome<T>.FromResult(await fallback(outcome, context).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }
    }
}

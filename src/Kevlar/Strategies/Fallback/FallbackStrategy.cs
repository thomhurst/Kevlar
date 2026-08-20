using System.Diagnostics;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class FallbackStrategy<TResult> : Strategy
{
    private readonly Func<Outcome<TResult>, KevlarContext, ValueTask<TResult>> _fallback;
    private readonly OutcomeJudge _judge;
    private readonly Action<FallbackEvent<TResult>>? _onFallback;

    public FallbackStrategy(
        Func<Outcome<TResult>, KevlarContext, ValueTask<TResult>> fallback,
        OutcomeJudge judge,
        Action<FallbackEvent<TResult>>? onFallback)
    {
        _fallback = fallback;
        _judge = judge;
        _onFallback = onFallback;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool IsFallback => true;

    public override string Describe() => "Fallback";

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var outcome = await next.InvokeAsync(context).ConfigureAwait(false);

        if (!_judge.ShouldHandle(in outcome))
        {
            return outcome;
        }

        Debug.Assert(typeof(T) == typeof(TResult), "Fallback strategies only execute inside a matching Shield<TResult>.");
        var typedOutcome = (Outcome<TResult>)(object)outcome;

        KevlarMetrics.Fallback(context.ShieldName);
        _onFallback?.Invoke(new FallbackEvent<TResult>(typedOutcome, context));

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

/// <summary>
/// Fallback for void executions on a non-generic <see cref="Shield"/>: runs an alternative action
/// in place of a handled failure. Result-returning executions are rejected with a descriptive
/// error, because a void fallback cannot produce a result value.
/// </summary>
internal sealed class VoidFallbackStrategy : Strategy
{
    private readonly Func<Exception, CancellationToken, ValueTask> _fallback;
    private readonly OutcomeJudge _judge;

    public VoidFallbackStrategy(Func<Exception, CancellationToken, ValueTask> fallback, OutcomeJudge judge)
    {
        _fallback = fallback;
        _judge = judge;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool IsFallback => true;

    public override string Describe() => "Fallback";

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var outcome = await next.InvokeAsync(context).ConfigureAwait(false);

        if (outcome.Exception is not { } exception || !_judge.ShouldHandle(in outcome))
        {
            return outcome;
        }

        KevlarMetrics.Fallback(context.ShieldName);

        if (typeof(T) != typeof(Nothing))
        {
            return Outcome<T>.FromException(new InvalidOperationException(
                "Fallback on a non-generic Shield applies only to void executions. " +
                "For executions that return a value, build a result-aware shield with " +
                "Shield.For<T>() and use its Fallback overloads."));
        }

        try
        {
            await _fallback(exception, context.CancellationToken).ConfigureAwait(false);
            return Outcome<T>.FromResult(default!);
        }
        catch (Exception fallbackFailure)
        {
            return Outcome<T>.FromException(fallbackFailure);
        }
    }
}

using System.Diagnostics;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class FallbackStrategy<TResult> : Strategy
{
    internal override bool InvokesContinuationAtMostOnce => true;

    private readonly Func<Outcome<TResult>, KevlarContext, ValueTask<TResult>> _fallback;
    private readonly OutcomeJudge _judge;
    private readonly Action<FallbackEvent<TResult>>? _onFallback;
    private readonly Func<FallbackEvent<TResult>, ValueTask>? _onFallbackAsync;

    public FallbackStrategy(
        Func<Outcome<TResult>, KevlarContext, ValueTask<TResult>> fallback,
        OutcomeJudge judge,
        Action<FallbackEvent<TResult>>? onFallback,
        Func<FallbackEvent<TResult>, ValueTask>? onFallbackAsync)
    {
        _fallback = fallback;
        _judge = judge;
        _onFallback = onFallback;
        _onFallbackAsync = onFallbackAsync;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool IsFallback => true;

    public override string Describe() => "Fallback";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var execution = next.InvokeAsync(context);
        return execution.IsCompletedSuccessfully
            ? HandleOutcome(execution.Result, context)
            : AwaitOutcomeAsync(execution, context);
    }

    private async ValueTask<Outcome<T>> AwaitOutcomeAsync<T>(ValueTask<Outcome<T>> execution, KevlarContext context)
    {
        var outcome = await execution.ConfigureAwait(false);
        return await HandleOutcome(outcome, context).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> HandleOutcome<T>(Outcome<T> outcome, KevlarContext context)
    {
        if (!_judge.ShouldHandle(in outcome))
        {
            return new ValueTask<Outcome<T>>(outcome);
        }

        Debug.Assert(typeof(T) == typeof(TResult), "Fallback strategies only execute inside a matching Shield<TResult>.");
        var typedOutcome = (Outcome<TResult>)(object)outcome;

        KevlarMetrics.Fallback(context.ShieldName);
        if (_onFallback is not null || _onFallbackAsync is not null)
        {
            var fallbackEvent = new FallbackEvent<TResult>(typedOutcome, context);
            _onFallback?.Invoke(fallbackEvent);

            if (_onFallbackAsync is not null)
            {
                var notification = _onFallbackAsync(fallbackEvent);
                if (!notification.IsCompletedSuccessfully)
                {
                    return AwaitNotificationAsync(notification, outcome, context);
                }

                notification.GetAwaiter().GetResult();
            }
        }

        return InvokeFallback(outcome, context);
    }

    private ValueTask<Outcome<T>> InvokeFallback<T>(Outcome<T> outcome, KevlarContext context)
    {
        var fallback = (Func<Outcome<T>, KevlarContext, ValueTask<T>>)(object)_fallback;

        try
        {
            var execution = fallback(outcome, context);
            return execution.IsCompletedSuccessfully
                ? new ValueTask<Outcome<T>>(Outcome<T>.FromResult(execution.Result))
                : AwaitFallbackAsync(execution);
        }
        catch (Exception exception)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
        }
    }

    private async ValueTask<Outcome<T>> AwaitNotificationAsync<T>(
        ValueTask notification,
        Outcome<T> outcome,
        KevlarContext context)
    {
        await notification.ConfigureAwait(false);
        return await InvokeFallback(outcome, context).ConfigureAwait(false);
    }

    private static async ValueTask<Outcome<T>> AwaitFallbackAsync<T>(ValueTask<T> execution)
    {
        try
        {
            return Outcome<T>.FromResult(await execution.ConfigureAwait(false));
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
    internal override bool InvokesContinuationAtMostOnce => true;

    private readonly Func<Exception, CancellationToken, ValueTask> _fallback;
    private readonly OutcomeJudge _judge;
    private readonly Action<FallbackEvent>? _onFallback;
    private readonly Func<FallbackEvent, ValueTask>? _onFallbackAsync;

    public VoidFallbackStrategy(
        Func<Exception, CancellationToken, ValueTask> fallback,
        OutcomeJudge judge,
        Action<FallbackEvent>? onFallback,
        Func<FallbackEvent, ValueTask>? onFallbackAsync)
    {
        _fallback = fallback;
        _judge = judge;
        _onFallback = onFallback;
        _onFallbackAsync = onFallbackAsync;
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

        if (_onFallback is not null || _onFallbackAsync is not null)
        {
            var fallbackEvent = new FallbackEvent(exception, context);
            _onFallback?.Invoke(fallbackEvent);

            if (_onFallbackAsync is not null)
            {
                await _onFallbackAsync(fallbackEvent).ConfigureAwait(false);
            }
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

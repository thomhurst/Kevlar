using System.Diagnostics;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class FallbackStrategy<TResult> : Strategy, IFallbackStrategyInspection
{
    protected internal override bool InvokesContinuationAtMostOnce => true;

    private readonly Func<Outcome<TResult>, KevlarContext, ValueTask<TResult>> _fallback;
    private readonly OutcomeJudge _judge;
    private readonly Action<FallbackEvent<TResult>>? _onFallback;
    private readonly Func<FallbackEvent<TResult>, ValueTask>? _onFallbackAsync;
    private readonly string _telemetryName;
    private readonly bool _fallbackIsAsync;

    public FallbackStrategy(
        Func<Outcome<TResult>, KevlarContext, ValueTask<TResult>> fallback,
        OutcomeJudge judge,
        Action<FallbackEvent<TResult>>? onFallback,
        Func<FallbackEvent<TResult>, ValueTask>? onFallbackAsync,
        bool fallbackIsAsync,
        bool hasHandlingOverride = false,
        string? telemetryName = null)
    {
        _fallback = fallback;
        _judge = judge;
        _onFallback = onFallback;
        _onFallbackAsync = onFallbackAsync;
        _telemetryName = telemetryName ?? "Fallback";
        _fallbackIsAsync = fallbackIsAsync;
        HasHandlingOverride = hasHandlingOverride;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    internal override bool IsFallback => true;

    protected internal override string? SynchronousExecutionUnsupportedReason =>
        _fallbackIsAsync
            ? "Fallback recovery delegate"
            : _onFallbackAsync is null
                ? null
                : "FallbackOptions.OnFallbackAsync";

    Type? IFallbackStrategyInspection.ResultType => typeof(TResult);

    bool IFallbackStrategyInspection.HasNotification =>
        _onFallback is not null || _onFallbackAsync is not null;

    public override string Describe()
    {
        // Keep FallbackTo's execution delegate identical to the original captured-lambda fast path.
        // Description runs off-path, so identifying its compiler-generated method adds no execution cost.
        return _fallback.Method.Name.StartsWith("<FallbackTo>", StringComparison.Ordinal)
            ? "Fallback(value)"
            : "Fallback";
    }

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var strategyIndex = context.StrategyIndex;
        var execution = next.InvokeAsync(context);
        return execution.IsCompletedSuccessfully
            ? HandleOutcome(execution.Result, context, strategyIndex)
            : AwaitOutcomeAsync(execution, context, strategyIndex);
    }

    private async ValueTask<Outcome<T>> AwaitOutcomeAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        int strategyIndex)
    {
        var outcome = await execution.ConfigureAwait(false);
        return await HandleOutcome(outcome, context, strategyIndex).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> HandleOutcome<T>(Outcome<T> outcome, KevlarContext context, int strategyIndex)
    {
        if (!_judge.ShouldHandle(in outcome, context, attempt: 0, strategyIndex))
        {
            return new ValueTask<Outcome<T>>(outcome);
        }

        Debug.Assert(typeof(T) == typeof(TResult), "Fallback strategies only execute inside a matching Shield<TResult>.");
        var typedOutcome = (Outcome<TResult>)(object)outcome;

        KevlarMetrics.Fallback(context, _telemetryName, outcome.IsSuccess, outcome.Exception);
        if (_onFallback is not null || _onFallbackAsync is not null)
        {
            var fallbackEvent = new FallbackEvent<TResult>(typedOutcome, context);
            CallbackInvoker.Invoke(_onFallback, fallbackEvent, CallbackErrorKind.Fallback, context);
            var notification = CallbackInvoker.InvokeAsync(
                _onFallbackAsync,
                fallbackEvent,
                CallbackErrorKind.Fallback,
                context);
            if (!notification.IsCompletedSuccessfully)
            {
                return AwaitNotificationAsync(notification, outcome, context);
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
internal sealed class VoidFallbackStrategy : Strategy, IFallbackStrategyInspection
{
    protected internal override bool InvokesContinuationAtMostOnce => true;

    private readonly Func<Exception, CancellationToken, ValueTask> _fallback;
    private readonly OutcomeJudge _judge;
    private readonly Action<FallbackEvent>? _onFallback;
    private readonly Func<FallbackEvent, ValueTask>? _onFallbackAsync;
    private readonly string _telemetryName;
    private readonly bool _fallbackIsAsync;

    public VoidFallbackStrategy(
        Func<Exception, CancellationToken, ValueTask> fallback,
        OutcomeJudge judge,
        Action<FallbackEvent>? onFallback,
        Func<FallbackEvent, ValueTask>? onFallbackAsync,
        bool fallbackIsAsync,
        bool hasHandlingOverride = false,
        string? telemetryName = null)
    {
        _fallback = fallback;
        _judge = judge;
        _onFallback = onFallback;
        _onFallbackAsync = onFallbackAsync;
        _telemetryName = telemetryName ?? "Fallback";
        _fallbackIsAsync = fallbackIsAsync;
        HasHandlingOverride = hasHandlingOverride;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    internal override bool IsFallback => true;

    protected internal override string? SynchronousExecutionUnsupportedReason =>
        _fallbackIsAsync
            ? "Fallback recovery delegate"
            : _onFallbackAsync is null
                ? null
                : "FallbackOptions.OnFallbackAsync";

    Type? IFallbackStrategyInspection.ResultType => null;

    bool IFallbackStrategyInspection.HasNotification =>
        _onFallback is not null || _onFallbackAsync is not null;

    public override string Describe() => "Fallback";

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var strategyIndex = context.StrategyIndex;
        var outcome = await next.InvokeAsync(context).ConfigureAwait(false);

        if (outcome.Exception is not { } exception
            || !_judge.ShouldHandle(in outcome, context, attempt: 0, strategyIndex))
        {
            return outcome;
        }

        if (typeof(T) != typeof(Nothing))
        {
            return Outcome<T>.FromException(new InvalidOperationException(
                "Fallback on a non-generic Shield applies only to void executions. " +
                "For executions that return a value, build a result-aware shield with " +
                "Shield.For<T>() and use its Fallback overloads."));
        }

        KevlarMetrics.Fallback(context, _telemetryName, isSuccess: false, exception);

        if (_onFallback is not null || _onFallbackAsync is not null)
        {
            var fallbackEvent = new FallbackEvent(exception, context);
            CallbackInvoker.Invoke(_onFallback, fallbackEvent, CallbackErrorKind.Fallback, context);
            await CallbackInvoker.InvokeAsync(
                _onFallbackAsync,
                fallbackEvent,
                CallbackErrorKind.Fallback,
                context).ConfigureAwait(false);
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

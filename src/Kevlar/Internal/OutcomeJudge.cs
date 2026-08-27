using System.Runtime.CompilerServices;

namespace Kevlar.Internal;

/// <summary>
/// Decides whether an outcome counts as a failure that reactive strategies
/// (retry, circuit breaker, hedging, fallback) should act upon.
/// </summary>
internal abstract class OutcomeJudge
{
    /// <summary>The default: handle ordinary errors, but not cancellation, rejections, or fatal exceptions.</summary>
    public static readonly OutcomeJudge Default = new DefaultJudge();

    /// <summary>
    /// A human-readable rendering of the clause — the terms the caller wrote, joined with
    /// <c>" | "</c> — for pipeline descriptions. <see langword="null"/> when the clause adds
    /// nothing worth printing, which covers default handling and local option overrides.
    /// </summary>
    public virtual string? Description => null;

    public abstract bool ShouldHandle<T>(
        in Outcome<T> outcome,
        KevlarContext? context,
        int attempt,
        int strategyIndex);

    public bool ShouldHandle<T>(in Outcome<T> outcome) =>
        ShouldHandle(in outcome, context: null, attempt: 0, strategyIndex: -1);

    public virtual bool IsContextAware => false;

    protected internal static void ReportPredicateFailure(Exception exception)
    {
        try
        {
            System.Diagnostics.Trace.TraceError(
                "Kevlar handling predicate failed and was treated as not handled: {0}",
                exception);
        }
        catch
        {
            // Diagnostics must not change the protected execution's outcome.
        }
    }

    private sealed class DefaultJudge : OutcomeJudge
    {
        public override bool ShouldHandle<T>(in Outcome<T> outcome, KevlarContext? context, int attempt, int strategyIndex) =>
            outcome.Exception is { } exception && IsOrdinaryError(exception);

        private static bool IsOrdinaryError(Exception exception) =>
            exception is not (
                OperationCanceledException
                or ExecutionRejectedException
                or OutOfMemoryException
                or InsufficientExecutionStackException
                or StackOverflowException
                or ThreadAbortException
                or AccessViolationException);
    }
}

/// <summary>Handles outcomes whose exception matches a caller-supplied predicate.</summary>
internal sealed class ExceptionJudge : OutcomeJudge
{
    private readonly Func<Exception, bool> _predicate;
    private readonly string? _description;

    public ExceptionJudge(Func<Exception, bool> predicate, string? description = null)
    {
        _predicate = predicate;
        _description = description;
    }

    public override string? Description => _description;

    public override bool ShouldHandle<T>(in Outcome<T> outcome, KevlarContext? context, int attempt, int strategyIndex) =>
        outcome.Exception is { } exception && _predicate(exception);
}

/// <summary>Handles exceptions using the active execution and strategy context.</summary>
internal sealed class ContextExceptionJudge : OutcomeJudge
{
    private readonly Func<Exception, bool>? _exceptionPredicate;
    private readonly Func<HandlingEvent, bool> _predicate;
    private readonly string? _description;

    public ContextExceptionJudge(
        Func<Exception, bool>? exceptionPredicate,
        Func<HandlingEvent, bool> predicate,
        string? description = null)
    {
        _exceptionPredicate = exceptionPredicate;
        _predicate = predicate;
        _description = description;
    }

    public override string? Description => _description;

    public override bool IsContextAware => true;

    public override bool ShouldHandle<T>(
        in Outcome<T> outcome,
        KevlarContext? context,
        int attempt,
        int strategyIndex) =>
        outcome.Exception is { } exception
        && ((_exceptionPredicate?.Invoke(exception) ?? false)
            || (context is not null
                && InvokeSafely(new HandlingEvent(exception, context, attempt, strategyIndex))));

    private bool InvokeSafely(HandlingEvent handlingEvent)
    {
        try
        {
            return _predicate(handlingEvent);
        }
        catch (Exception exception)
        {
            ReportPredicateFailure(exception);
            return false;
        }
    }
}

/// <summary>
/// Handles outcomes by exception predicate and/or typed result predicate. Only outcomes of the
/// declared result type are inspected; each predicate applies only when it was supplied.
/// </summary>
internal sealed class TypedJudge<TResult> : OutcomeJudge
{
    private readonly Func<Exception, bool>? _exceptionPredicate;
    private readonly Func<TResult, bool>? _resultPredicate;
    private readonly Func<HandlingEvent<TResult>, bool>? _contextPredicate;
    private readonly string? _description;

    public TypedJudge(
        Func<Exception, bool>? exceptionPredicate,
        Func<TResult, bool>? resultPredicate,
        string? description = null,
        Func<HandlingEvent<TResult>, bool>? contextPredicate = null)
    {
        _exceptionPredicate = exceptionPredicate;
        _resultPredicate = resultPredicate;
        _contextPredicate = contextPredicate;
        _description = description;
    }

    public override string? Description => _description;

    public override bool IsContextAware => _contextPredicate is not null;

    public override bool ShouldHandle<T>(
        in Outcome<T> outcome,
        KevlarContext? context,
        int attempt,
        int strategyIndex)
    {
        if (_contextPredicate is not null && context is not null && typeof(T) == typeof(TResult))
        {
            var typedOutcome = Unsafe.As<Outcome<T>, Outcome<TResult>>(
                ref Unsafe.AsRef(in outcome));
            try
            {
                if (_contextPredicate(new HandlingEvent<TResult>(typedOutcome, context, attempt, strategyIndex)))
                {
                    return true;
                }
            }
            catch (Exception predicateException)
            {
                ReportPredicateFailure(predicateException);
            }
        }

        if (outcome.Exception is { } exception)
        {
            return _exceptionPredicate?.Invoke(exception) ?? false;
        }

        if (_resultPredicate is null || typeof(T) != typeof(TResult))
        {
            return false;
        }

        var predicate = (Func<T, bool>)(object)_resultPredicate;
        return predicate(outcome.Result!);
    }
}

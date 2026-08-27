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

    /// <summary>The fallback default: also handle execution rejections, but not cancellation or fatal exceptions.</summary>
    public static readonly OutcomeJudge FallbackDefault = new FallbackDefaultJudge();

    internal static bool FallbackHandlesEveryOutcomeHandledBy(
        OutcomeJudge fallbackJudge,
        OutcomeJudge outerJudge) =>
        ReferenceEquals(fallbackJudge, outerJudge)
        || (ReferenceEquals(fallbackJudge, FallbackDefault)
            && ReferenceEquals(outerJudge, Default));

    /// <summary>
    /// A human-readable rendering of the clause — the terms the caller wrote, joined with
    /// <c>" | "</c> — for pipeline descriptions. <see langword="null"/> when the clause adds
    /// nothing worth printing, which covers default handling and local option overrides.
    /// </summary>
    public virtual string? Description => null;

    public bool ShouldHandle<T>(
        in Outcome<T> outcome,
        KevlarContext? context,
        int attempt,
        int strategyIndex)
    {
        if (outcome.Exception is { } exception
            && SynchronousExecutionGuard.IsRejection(exception))
        {
            return false;
        }

        return ShouldHandleCore(in outcome, context, attempt, strategyIndex);
    }

    protected abstract bool ShouldHandleCore<T>(
        in Outcome<T> outcome,
        KevlarContext? context,
        int attempt,
        int strategyIndex);

    public bool ShouldHandle<T>(in Outcome<T> outcome) =>
        ShouldHandle(in outcome, context: null, attempt: 0, strategyIndex: -1);

    public virtual bool IsContextAware => false;

    private static void ReportPredicateFailure(
        Exception exception,
        KevlarContext? context,
        int attempt,
        int strategyIndex)
    {
        if (context is not null)
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.HandlingPredicate,
                context,
                exception,
                "HandlingPredicate",
                attemptNumber: attempt,
                strategyIndex: strategyIndex);
        }
    }

    protected static bool EvaluatePredicates<T>(
        Func<T, bool>[] predicates,
        T value,
        KevlarContext? context,
        int attempt,
        int strategyIndex)
    {
        foreach (var predicate in predicates)
        {
            try
            {
                if (predicate(value))
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                ReportPredicateFailure(exception, context, attempt, strategyIndex);
            }
        }

        return false;
    }

    private static bool IsOrdinaryError(Exception exception) =>
        exception is not (
            OperationCanceledException
            or ExecutionRejectedException
            or OutOfMemoryException
            or InsufficientExecutionStackException
            or StackOverflowException
            or ThreadAbortException
            or AccessViolationException);

    private sealed class DefaultJudge : OutcomeJudge
    {
        protected override bool ShouldHandleCore<T>(in Outcome<T> outcome, KevlarContext? context, int attempt, int strategyIndex) =>
            outcome.Exception is { } exception && IsOrdinaryError(exception);
    }

    private sealed class FallbackDefaultJudge : OutcomeJudge
    {
        protected override bool ShouldHandleCore<T>(in Outcome<T> outcome, KevlarContext? context, int attempt, int strategyIndex) =>
            outcome.Exception is { } exception
            && (exception is ExecutionRejectedException || IsOrdinaryError(exception));
    }
}

/// <summary>Handles outcomes whose exception matches a caller-supplied predicate.</summary>
internal sealed class ExceptionJudge : OutcomeJudge
{
    private readonly Func<Exception, bool>[] _predicates;
    private readonly string? _description;

    public ExceptionJudge(Func<Exception, bool> predicate, string? description = null)
        : this([predicate], description)
    {
    }

    public ExceptionJudge(Func<Exception, bool>[] predicates, string? description = null)
    {
        _predicates = predicates;
        _description = description;
    }

    public override string? Description => _description;

    protected override bool ShouldHandleCore<T>(in Outcome<T> outcome, KevlarContext? context, int attempt, int strategyIndex) =>
        outcome.Exception is { } exception
        && EvaluatePredicates(_predicates, exception, context, attempt, strategyIndex);
}

/// <summary>Handles exceptions using the active execution and strategy context.</summary>
internal sealed class ContextExceptionJudge : OutcomeJudge
{
    private readonly Func<Exception, bool>[] _exceptionPredicates;
    private readonly Func<HandlingEvent, bool>[] _contextPredicates;
    private readonly string? _description;

    public ContextExceptionJudge(
        Func<Exception, bool>? exceptionPredicate,
        Func<HandlingEvent, bool> predicate,
        string? description = null)
        : this(
            exceptionPredicate is null ? [] : [exceptionPredicate],
            [predicate],
            description)
    {
    }

    public ContextExceptionJudge(
        Func<Exception, bool>[] exceptionPredicates,
        Func<HandlingEvent, bool>[] contextPredicates,
        string? description = null)
    {
        _exceptionPredicates = exceptionPredicates;
        _contextPredicates = contextPredicates;
        _description = description;
    }

    public override string? Description => _description;

    public override bool IsContextAware => true;

    protected override bool ShouldHandleCore<T>(
        in Outcome<T> outcome,
        KevlarContext? context,
        int attempt,
        int strategyIndex) =>
        outcome.Exception is { } exception
        && (EvaluatePredicates(
                _exceptionPredicates,
                exception,
                context,
                attempt,
                strategyIndex)
            || context is not null
            && EvaluatePredicates(
                _contextPredicates,
                new HandlingEvent(exception, context, attempt, strategyIndex),
                context,
                attempt,
                strategyIndex));
}

/// <summary>
/// Handles outcomes by exception predicate and/or typed result predicate. Only outcomes of the
/// declared result type are inspected; each predicate applies only when it was supplied.
/// </summary>
internal sealed class TypedJudge<TResult> : OutcomeJudge
{
    private readonly Func<Exception, bool>[] _exceptionPredicates;
    private readonly Func<TResult, bool>[] _resultPredicates;
    private readonly Func<HandlingEvent<TResult>, bool>[] _contextPredicates;
    private readonly string? _description;

    public TypedJudge(
        Func<Exception, bool>? exceptionPredicate,
        Func<TResult, bool>? resultPredicate,
        string? description = null,
        Func<HandlingEvent<TResult>, bool>? contextPredicate = null)
        : this(
            exceptionPredicate is null ? [] : [exceptionPredicate],
            resultPredicate is null ? [] : [resultPredicate],
            description,
            contextPredicate is null ? [] : [contextPredicate])
    {
    }

    public TypedJudge(
        Func<Exception, bool>[] exceptionPredicates,
        Func<TResult, bool>[] resultPredicates,
        string? description = null,
        Func<HandlingEvent<TResult>, bool>[]? contextPredicates = null)
    {
        _exceptionPredicates = exceptionPredicates;
        _resultPredicates = resultPredicates;
        _contextPredicates = contextPredicates ?? [];
        _description = description;
    }

    public override string? Description => _description;

    public override bool IsContextAware => _contextPredicates.Length != 0;

    protected override bool ShouldHandleCore<T>(
        in Outcome<T> outcome,
        KevlarContext? context,
        int attempt,
        int strategyIndex)
    {
        if (_contextPredicates.Length != 0 && context is not null && typeof(T) == typeof(TResult))
        {
            var typedOutcome = Unsafe.As<Outcome<T>, Outcome<TResult>>(
                ref Unsafe.AsRef(in outcome));
            if (EvaluatePredicates(
                _contextPredicates,
                new HandlingEvent<TResult>(typedOutcome, context, attempt, strategyIndex),
                context,
                attempt,
                strategyIndex))
            {
                return true;
            }
        }

        if (outcome.Exception is { } exception)
        {
            return EvaluatePredicates(
                _exceptionPredicates,
                exception,
                context,
                attempt,
                strategyIndex);
        }

        if (_resultPredicates.Length == 0 || typeof(T) != typeof(TResult))
        {
            return false;
        }

        var predicates = (Func<T, bool>[])(object)_resultPredicates;
        return EvaluatePredicates(
            predicates,
            outcome.Result!,
            context,
            attempt,
            strategyIndex);
    }
}

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

    public abstract bool ShouldHandle<T>(in Outcome<T> outcome);

    private sealed class DefaultJudge : OutcomeJudge
    {
        public override bool ShouldHandle<T>(in Outcome<T> outcome) =>
            outcome.Exception is { } exception && IsOrdinaryError(exception);

        private static bool IsOrdinaryError(Exception exception) => exception is not (
            OperationCanceledException
            or CircuitOpenException
            or RateLimitExceededException
            or ConcurrencyLimitExceededException
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

    public override bool ShouldHandle<T>(in Outcome<T> outcome) =>
        outcome.Exception is { } exception && _predicate(exception);
}

/// <summary>
/// Handles outcomes by exception predicate and/or typed result predicate. Only outcomes of the
/// declared result type are inspected; each predicate applies only when it was supplied.
/// </summary>
internal sealed class TypedJudge<TResult> : OutcomeJudge
{
    private readonly Func<Exception, bool>? _exceptionPredicate;
    private readonly Func<TResult, bool>? _resultPredicate;
    private readonly string? _description;

    public TypedJudge(
        Func<Exception, bool>? exceptionPredicate,
        Func<TResult, bool>? resultPredicate,
        string? description = null)
    {
        _exceptionPredicate = exceptionPredicate;
        _resultPredicate = resultPredicate;
        _description = description;
    }

    public override string? Description => _description;

    public override bool ShouldHandle<T>(in Outcome<T> outcome)
    {
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

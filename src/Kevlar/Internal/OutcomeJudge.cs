namespace Kevlar.Internal;

/// <summary>
/// Decides whether an outcome counts as a failure that reactive strategies
/// (retry, circuit breaker, hedging, fallback) should act upon.
/// </summary>
internal abstract class OutcomeJudge
{
    /// <summary>The default: handle any exception except <see cref="OperationCanceledException"/>.</summary>
    public static readonly OutcomeJudge Default = new DefaultJudge();

    public abstract bool ShouldHandle<T>(in Outcome<T> outcome);

    private sealed class DefaultJudge : OutcomeJudge
    {
        public override bool ShouldHandle<T>(in Outcome<T> outcome) =>
            outcome.Exception is { } exception && exception is not OperationCanceledException;
    }
}

/// <summary>Handles outcomes whose exception matches a caller-supplied predicate.</summary>
internal sealed class ExceptionJudge : OutcomeJudge
{
    private readonly Func<Exception, bool> _predicate;

    public ExceptionJudge(Func<Exception, bool> predicate) => _predicate = predicate;

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

    public TypedJudge(Func<Exception, bool>? exceptionPredicate, Func<TResult, bool>? resultPredicate)
    {
        _exceptionPredicate = exceptionPredicate;
        _resultPredicate = resultPredicate;
    }

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

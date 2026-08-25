namespace Kevlar.Strategies;

internal sealed class CircuitBreakerBreakDurationGenerator
{
    private readonly Delegate _generator;
    private readonly Type? _resultType;
    private readonly bool _isAsynchronous;

    private CircuitBreakerBreakDurationGenerator(Delegate generator, Type? resultType, bool isAsynchronous)
    {
        _generator = generator;
        _resultType = resultType;
        _isAsynchronous = isAsynchronous;
    }

    internal bool IsAsynchronous => _isAsynchronous;

    internal static CircuitBreakerBreakDurationGenerator Create(
        Func<CircuitBreakerBreakDurationEvent, ValueTask<TimeSpan>> generator) =>
        new(generator, resultType: null, isAsynchronous: true);

    internal static CircuitBreakerBreakDurationGenerator Create(
        Func<CircuitBreakerBreakDurationEvent, TimeSpan> generator) =>
        new(generator, resultType: null, isAsynchronous: false);

    internal static CircuitBreakerBreakDurationGenerator Create<TResult>(
        Func<CircuitBreakerBreakDurationEvent<TResult>, ValueTask<TimeSpan>> generator) =>
        new(generator, typeof(TResult), isAsynchronous: true);

    internal static CircuitBreakerBreakDurationGenerator Create<TResult>(
        Func<CircuitBreakerBreakDurationEvent<TResult>, TimeSpan> generator) =>
        new(generator, typeof(TResult), isAsynchronous: false);

    internal ValueTask<TimeSpan> Invoke<TResult>(
        in Outcome<TResult> outcome,
        in CircuitBreakerFailureStatistics statistics,
        KevlarContext context)
    {
        if (_resultType is null)
        {
            var item = new CircuitBreakerBreakDurationEvent(
                outcome.Exception,
                outcome.Exception is null ? outcome.Result : null,
                statistics.FailureRate,
                statistics.FailureCount,
                statistics.ConsecutiveFailures,
                context);
            return _isAsynchronous
                ? ((Func<CircuitBreakerBreakDurationEvent, ValueTask<TimeSpan>>)_generator)(item)
                : new ValueTask<TimeSpan>(
                    ((Func<CircuitBreakerBreakDurationEvent, TimeSpan>)_generator)(item));
        }

        if (_resultType != typeof(TResult))
        {
            throw new InvalidOperationException(
                $"The circuit-breaker break-duration generator was created for '{_resultType}', " +
                $"but this execution returns '{typeof(TResult)}'.");
        }

        var typedItem = new CircuitBreakerBreakDurationEvent<TResult>(
            outcome,
            statistics.FailureRate,
            statistics.FailureCount,
            statistics.ConsecutiveFailures,
            context);
        return _isAsynchronous
            ? ((Func<CircuitBreakerBreakDurationEvent<TResult>, ValueTask<TimeSpan>>)_generator)(typedItem)
            : new ValueTask<TimeSpan>(
                ((Func<CircuitBreakerBreakDurationEvent<TResult>, TimeSpan>)_generator)(typedItem));
    }
}

internal readonly record struct CircuitBreakerFailureStatistics(
    double FailureRate,
    long FailureCount,
    int ConsecutiveFailures);

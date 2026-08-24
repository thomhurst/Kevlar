using Kevlar.Internal;
using Reservoir;

namespace Kevlar.Strategies;

/// <summary>
/// Cooperative timeout: replaces the context's cancellation token with one that is cancelled
/// after the configured time. The executed delegate must observe the token it is handed.
/// If the delegate completes successfully despite the token firing, the result is still delivered.
/// </summary>
internal sealed class TimeoutStrategy : Strategy
{
    protected internal override bool InvokesContinuationAtMostOnce => true;

    private readonly TimeSpan _timeout;
    private readonly Func<KevlarContext, ValueTask<TimeSpan>>? _timeoutGenerator;
    private readonly Action<TimeoutEvent>? _onTimeout;
    private readonly Func<TimeoutEvent, ValueTask>? _onTimeoutAsync;

    public TimeoutStrategy(TimeoutOptions options)
    {
        Throw.IfOutOfRange(options.Timeout <= TimeSpan.Zero, nameof(options), "Timeout must be positive.");
        Throw.IfOutOfRange(options.Timeout > DelayHelper.MaximumDelay, nameof(options.Timeout), "Timeout exceeds the runtime timer limit.");
        _timeout = options.Timeout;
        _timeoutGenerator = options.TimeoutGenerator;
        _onTimeout = options.OnTimeout;
        _onTimeoutAsync = options.OnTimeoutAsync;
    }

    public override string Describe() => _timeoutGenerator is null
        ? $"Timeout({DescribeHelper.Time(_timeout)})"
        : "Timeout(dynamic)";

    internal TimeSpan Timeout => _timeout;

    internal bool HasTimeoutGenerator => _timeoutGenerator is not null;

    internal bool HasNotification => _onTimeout is not null || _onTimeoutAsync is not null;

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (_timeoutGenerator is null)
        {
            return ExecuteWithTimeout(next, context, _timeout);
        }

        var generation = _timeoutGenerator(context);
        if (!generation.IsCompletedSuccessfully)
        {
            return AwaitGenerationAsync(generation, next, context);
        }

        var timeout = generation.Result;
        ValidateGeneratedTimeout(timeout);
        if (context.CancellationToken.IsCancellationRequested)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(
                new OperationCanceledException(context.CancellationToken)));
        }

        return ExecuteWithTimeout(next, context, timeout);
    }

    private async ValueTask<Outcome<T>> AwaitGenerationAsync<T, TState>(
        ValueTask<TimeSpan> generation,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var timeout = await generation.ConfigureAwait(false);
        ValidateGeneratedTimeout(timeout);
        if (context.CancellationToken.IsCancellationRequested)
        {
            return Outcome<T>.FromException(new OperationCanceledException(context.CancellationToken));
        }

        return await ExecuteWithTimeout(next, context, timeout).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> ExecuteWithTimeout<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        TimeSpan timeout)
    {
        var priorToken = context.CancellationToken;
        var usesSystemTime = ReferenceEquals(context.TimeProvider, TimeProvider.System);
        var timeoutSource = usesSystemTime
            ? CancellationTokenSourcePool.Shared.RentLinked(priorToken)
            : CancellationTokenSource.CreateLinkedTokenSource(priorToken);
        ITimer? timer = null;
        ValueTask<Outcome<T>> execution;

        try
        {
            if (usesSystemTime)
            {
                timeoutSource.CancelAfter(timeout);
            }
            else
            {
                timer = context.TimeProvider.CreateTimer(
                    static state =>
                    {
                        try
                        {
                            ((CancellationTokenSource)state!).Cancel();
                        }
                        catch (ObjectDisposedException)
                        {
                            // A queued callback may outlive disposal of a custom provider's timer.
                        }
                    },
                    timeoutSource,
                    timeout,
                    System.Threading.Timeout.InfiniteTimeSpan);
            }

            context.CancellationToken = timeoutSource.Token;
            execution = next.InvokeAsync(context);
        }
        catch
        {
            Cleanup(context, priorToken, timeoutSource, timer);
            throw;
        }

        if (!execution.IsCompletedSuccessfully)
        {
            return AwaitAsync(execution, context, priorToken, timeoutSource, timer, timeout);
        }

        var outcome = execution.Result;
        if (outcome.Exception is not OperationCanceledException cancellationException)
        {
            Cleanup(context, priorToken, timeoutSource, timer);
            return new ValueTask<Outcome<T>>(outcome);
        }

        return CompleteCancellationAsync(
            outcome,
            cancellationException,
            context,
            priorToken,
            timeoutSource,
            timer,
            timeout);
    }

    private async ValueTask<Outcome<T>> AwaitAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        CancellationToken priorToken,
        CancellationTokenSource timeoutSource,
        ITimer? timer,
        TimeSpan timeout)
    {
        Outcome<T> outcome;

        try
        {
            outcome = await execution.ConfigureAwait(false);
        }
        catch
        {
            Cleanup(context, priorToken, timeoutSource, timer);
            throw;
        }

        if (outcome.Exception is not OperationCanceledException cancellationException)
        {
            Cleanup(context, priorToken, timeoutSource, timer);
            return outcome;
        }

        return await CompleteCancellationAsync(
            outcome,
            cancellationException,
            context,
            priorToken,
            timeoutSource,
            timer,
            timeout).ConfigureAwait(false);
    }

    private static void Cleanup(
        KevlarContext context,
        CancellationToken priorToken,
        CancellationTokenSource timeoutSource,
        ITimer? timer)
    {
        context.CancellationToken = priorToken;
        timer?.Dispose();
        timeoutSource.Dispose();
    }

    private ValueTask<Outcome<T>> CompleteCancellationAsync<T>(
        Outcome<T> outcome,
        OperationCanceledException cancellationException,
        KevlarContext context,
        CancellationToken priorToken,
        CancellationTokenSource timeoutSource,
        ITimer? timer,
        TimeSpan timeout)
    {
        context.CancellationToken = priorToken;
        timer?.Dispose();

        if (priorToken.IsCancellationRequested)
        {
            timeoutSource.Dispose();

            if (cancellationException.CancellationToken == priorToken)
            {
                return new ValueTask<Outcome<T>>(outcome);
            }

            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(new OperationCanceledException(
                cancellationException.Message,
                cancellationException,
                priorToken)));
        }

        var timedOut = timeoutSource.IsCancellationRequested
            && cancellationException.CancellationToken == timeoutSource.Token;
        timeoutSource.Dispose();

        if (timedOut)
        {
            KevlarMetrics.Timeout(context.ShieldName);
            var timeoutEvent = new TimeoutEvent(timeout, context);
            _onTimeout?.Invoke(timeoutEvent);

            if (_onTimeoutAsync is not null)
            {
                var notification = _onTimeoutAsync(timeoutEvent);
                if (!notification.IsCompletedSuccessfully)
                {
                    return AwaitTimeoutNotificationAsync<T>(notification, timeout);
                }

                notification.GetAwaiter().GetResult();
            }

            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(
                new TimeoutExceededException(timeout)));
        }

        return new ValueTask<Outcome<T>>(outcome);
    }

    private static async ValueTask<Outcome<T>> AwaitTimeoutNotificationAsync<T>(
        ValueTask notification,
        TimeSpan timeout)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromException(new TimeoutExceededException(timeout));
    }

    private static void ValidateGeneratedTimeout(TimeSpan timeout)
    {
        Throw.IfOutOfRange(timeout <= TimeSpan.Zero, nameof(timeout), "Generated timeout must be positive.");
        Throw.IfOutOfRange(timeout > DelayHelper.MaximumDelay, nameof(timeout), "Generated timeout exceeds the runtime timer limit.");
    }
}

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
    private readonly Func<TimeoutEvent, ValueTask<TimeSpan>>? _timeoutGenerator;
    private readonly Func<TimeoutEvent, ValueTask>? _onTimeout;
    private readonly string _telemetryName;

    public TimeoutStrategy(TimeoutOptions options)
    {
        ConfigurationValidation.ThrowIf(
            options.Timeout <= TimeSpan.Zero,
            typeof(TimeoutOptions),
            nameof(options.Timeout),
            options.Timeout,
            "must be positive");
        ConfigurationValidation.ThrowIf(
            options.Timeout > DelayHelper.MaximumDelay,
            typeof(TimeoutOptions),
            nameof(options.Timeout),
            options.Timeout,
            "must not exceed the runtime timer limit");
        _timeout = options.Timeout;
        _timeoutGenerator = options.TimeoutGenerator;
        _onTimeout = options.OnTimeout;
        _telemetryName = options.Name ?? "Timeout";
    }

    public override string Describe() => _timeoutGenerator is null
        ? $"Timeout({DescribeHelper.Time(_timeout)})"
        : "Timeout(dynamic)";

    internal TimeSpan Timeout => _timeout;

    internal bool HasTimeoutGenerator => _timeoutGenerator is not null;

    internal bool HasNotification => _onTimeout is not null;

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (_timeoutGenerator is null)
        {
            return ExecuteWithTimeout(next, context, _timeout);
        }

        var generatorEvent = new TimeoutEvent(_timeout, context);
        var generation = CallbackInvoker.InvokeGenerator(
            _timeoutGenerator,
            generatorEvent,
            context,
            "TimeoutOptions.TimeoutGenerator");
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
        var recordTimeoutIgnored = KevlarMetrics.TimeoutIgnoredEnabled(context);
        var startedAt = recordTimeoutIgnored ? context.TimeProvider.GetTimestamp() : 0;

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
            return AwaitAsync(
                execution,
                context,
                priorToken,
                timeoutSource,
                timer,
                timeout,
                startedAt,
                recordTimeoutIgnored);
        }

        var outcome = execution.Result;
        if (outcome.Exception is not OperationCanceledException cancellationException)
        {
            return new ValueTask<Outcome<T>>(CompleteNonCancellation(
                outcome,
                context,
                priorToken,
                timeoutSource,
                timer,
                startedAt,
                recordTimeoutIgnored));
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
        TimeSpan timeout,
        long startedAt,
        bool recordTimeoutIgnored)
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
            return CompleteNonCancellation(
                outcome,
                context,
                priorToken,
                timeoutSource,
                timer,
                startedAt,
                recordTimeoutIgnored);
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

    private Outcome<T> CompleteNonCancellation<T>(
        Outcome<T> outcome,
        KevlarContext context,
        CancellationToken priorToken,
        CancellationTokenSource timeoutSource,
        ITimer? timer,
        long startedAt,
        bool recordTimeoutIgnored)
    {
        context.CancellationToken = priorToken;
        timer?.Dispose();
        var timeoutIgnored = !priorToken.IsCancellationRequested && timeoutSource.IsCancellationRequested;
        timeoutSource.Dispose();

        if (timeoutIgnored && recordTimeoutIgnored)
        {
            KevlarMetrics.TimeoutIgnored(
                context,
                _telemetryName,
                context.TimeProvider.GetElapsedTime(startedAt),
                outcome.IsSuccess,
                outcome.Exception);
        }

        return outcome;
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

        var timedOut = timeoutSource.IsCancellationRequested;
        timeoutSource.Dispose();

        if (timedOut)
        {
            var timeoutException = new TimeoutExceededException(timeout, cancellationException);
            KevlarMetrics.Timeout(context, _telemetryName, timeout, timeoutException);
            var timeoutEvent = new TimeoutEvent(timeout, context);
            var notification = CallbackInvoker.InvokeAsync(
                _onTimeout,
                timeoutEvent,
                CallbackErrorKind.Timeout,
                context,
                "TimeoutOptions.OnTimeout");
            if (!notification.IsCompletedSuccessfully)
            {
                return AwaitTimeoutNotificationAsync<T>(notification, timeoutException);
            }

            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(timeoutException));
        }

        return new ValueTask<Outcome<T>>(outcome);
    }

    private static async ValueTask<Outcome<T>> AwaitTimeoutNotificationAsync<T>(
        ValueTask notification,
        TimeoutExceededException timeoutException)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromException(timeoutException);
    }

    private static void ValidateGeneratedTimeout(TimeSpan timeout)
    {
        ConfigurationValidation.ThrowIf(
            timeout <= TimeSpan.Zero,
            typeof(TimeoutOptions),
            nameof(TimeoutOptions.TimeoutGenerator),
            timeout,
            "must return a positive timeout");
        ConfigurationValidation.ThrowIf(
            timeout > DelayHelper.MaximumDelay,
            typeof(TimeoutOptions),
            nameof(TimeoutOptions.TimeoutGenerator),
            timeout,
            "must not return a timeout above the runtime timer limit");
    }
}

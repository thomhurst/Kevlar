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
    private readonly TimeSpan _timeout;
    private readonly Action<TimeoutEvent>? _onTimeout;

    public TimeoutStrategy(TimeoutOptions options)
    {
        Throw.IfOutOfRange(options.Timeout <= TimeSpan.Zero, nameof(options), "Timeout must be positive.");
        _timeout = options.Timeout;
        _onTimeout = options.OnTimeout;
    }

    public override string Describe() => $"Timeout({DescribeHelper.Time(_timeout)})";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
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
                timeoutSource.CancelAfter(_timeout);
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
                    _timeout,
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
            return AwaitAsync(execution, context, priorToken, timeoutSource, timer);
        }

        var outcome = execution.Result;
        if (outcome.Exception is not OperationCanceledException cancellationException)
        {
            Cleanup(context, priorToken, timeoutSource, timer);
            return new ValueTask<Outcome<T>>(outcome);
        }

        return new ValueTask<Outcome<T>>(CompleteCancellation(
            outcome,
            cancellationException,
            context,
            priorToken,
            timeoutSource,
            timer));
    }

    private async ValueTask<Outcome<T>> AwaitAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        CancellationToken priorToken,
        CancellationTokenSource timeoutSource,
        ITimer? timer)
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

        return CompleteCancellation(outcome, cancellationException, context, priorToken, timeoutSource, timer);
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

    private Outcome<T> CompleteCancellation<T>(
        Outcome<T> outcome,
        OperationCanceledException cancellationException,
        KevlarContext context,
        CancellationToken priorToken,
        CancellationTokenSource timeoutSource,
        ITimer? timer)
    {
        context.CancellationToken = priorToken;
        timer?.Dispose();

        if (priorToken.IsCancellationRequested)
        {
            timeoutSource.Dispose();

            if (cancellationException.CancellationToken == priorToken)
            {
                return outcome;
            }

            return Outcome<T>.FromException(new OperationCanceledException(
                cancellationException.Message,
                cancellationException,
                priorToken));
        }

        var timedOut = timeoutSource.IsCancellationRequested
            && cancellationException.CancellationToken == timeoutSource.Token;
        timeoutSource.Dispose();

        if (timedOut)
        {
            KevlarMetrics.Timeout(context.ShieldName);
            _onTimeout?.Invoke(new TimeoutEvent(_timeout, context));
            return Outcome<T>.FromException(new TimeoutExceededException(_timeout));
        }

        return outcome;
    }
}

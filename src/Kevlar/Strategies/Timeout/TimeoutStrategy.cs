using Kevlar.Internal;

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

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var priorToken = context.CancellationToken;
        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(priorToken);
        var timer = context.TimeProvider.CreateTimer(
            static state =>
            {
                try
                {
                    ((CancellationTokenSource)state!).Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The execution completed and disposed the source while the timer was firing.
                }
            },
            timeoutSource,
            _timeout,
            System.Threading.Timeout.InfiniteTimeSpan);

        Outcome<T> outcome;
        bool timedOut;

        try
        {
            context.CancellationToken = timeoutSource.Token;
            outcome = await next.InvokeAsync(context).ConfigureAwait(false);
        }
        finally
        {
            context.CancellationToken = priorToken;
            timer.Dispose();
            timedOut = timeoutSource.IsCancellationRequested && !priorToken.IsCancellationRequested;
            timeoutSource.Dispose();
        }

        if (timedOut && outcome.Exception is OperationCanceledException)
        {
            KevlarMetrics.Timeout(context.ShieldName);
            _onTimeout?.Invoke(new TimeoutEvent(_timeout, context));
            return Outcome<T>.FromException(new TimeoutExceededException(_timeout));
        }

        return outcome;
    }
}

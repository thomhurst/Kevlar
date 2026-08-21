using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class RetryStrategy : Strategy
{
    private readonly OutcomeJudge _judge;
    private readonly int _maxRetries;
    private readonly Backoff _backoff;
    private readonly TimeSpan? _maxDelay;
    private readonly Action<RetryEvent>? _onRetry;
    private readonly Func<RetryEvent, ValueTask>? _onRetryAsync;
    private readonly Func<RetryEvent, TimeSpan?>? _delayGenerator;

    public RetryStrategy(RetryOptions options, OutcomeJudge judge)
    {
        Throw.IfOutOfRange(options.MaxRetries < 0, nameof(options), "MaxRetries must not be negative.");
        Throw.IfNull(options.Backoff, nameof(options));

        _judge = judge;
        _maxRetries = options.MaxRetries;
        _backoff = options.Backoff;
        _maxDelay = options.MaxDelay;
        _onRetry = options.OnRetry;
        _onRetryAsync = options.OnRetryAsync;
        _delayGenerator = options.DelayGenerator;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    public override string Describe()
    {
        var cap = _maxDelay is { } max ? $", ≤{DescribeHelper.Time(max)}" : string.Empty;
        return _maxRetries == int.MaxValue
            ? $"RetryForever({_backoff}{cap})"
            : $"Retry({_maxRetries}, {_backoff}{cap})";
    }

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        for (var retriesUsed = 0; ; retriesUsed++)
        {
            var outcome = await next.InvokeAsync(context).ConfigureAwait(false);

            if (retriesUsed >= _maxRetries
                || !_judge.ShouldHandle(in outcome)
                || context.CancellationToken.IsCancellationRequested)
            {
                return outcome;
            }

            var attempt = retriesUsed + 1;
            KevlarMetrics.Retry(context.ShieldName);
            var delay = _backoff.GetDelay(attempt);

            if (_maxDelay is { } cap && delay > cap)
            {
                delay = cap;
            }

            if (_delayGenerator is not null || _onRetry is not null || _onRetryAsync is not null)
            {
                var result = outcome.Exception is null ? (object?)outcome.Result : null;

                if (_delayGenerator is not null
                    && _delayGenerator(new RetryEvent(attempt, delay, outcome.Exception, result, context)) is { } custom
                    && custom >= TimeSpan.Zero)
                {
                    // MaxDelay is an absolute bound: it also caps generator-supplied delays
                    // such as a server's Retry-After suggestion.
                    delay = _maxDelay is { } absolute && custom > absolute ? absolute : custom;
                }

                if (_onRetry is not null || _onRetryAsync is not null)
                {
                    var retryEvent = new RetryEvent(attempt, delay, outcome.Exception, result, context);
                    _onRetry?.Invoke(retryEvent);

                    if (_onRetryAsync is not null)
                    {
                        await _onRetryAsync(retryEvent).ConfigureAwait(false);
                    }
                }
            }

            if (delay > TimeSpan.Zero || context.CancellationToken.IsCancellationRequested)
            {
                try
                {
                    await DelayHelper.DelayAsync(context, delay).ConfigureAwait(false);
                }
                catch (OperationCanceledException cancelled)
                {
                    return Outcome<T>.FromException(cancelled);
                }
            }
        }
    }
}

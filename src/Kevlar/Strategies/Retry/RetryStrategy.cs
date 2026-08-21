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
    private readonly Func<RetryEvent, ValueTask<TimeSpan?>>? _delayGeneratorAsync;

    public RetryStrategy(RetryOptions options, OutcomeJudge judge)
    {
        Throw.IfOutOfRange(options.MaxRetries < 0, nameof(options), "MaxRetries must not be negative.");
        Throw.IfNull(options.Backoff, nameof(options));
        Throw.IfOutOfRange(options.MaxDelay.HasValue && options.MaxDelay.Value < TimeSpan.Zero, nameof(options.MaxDelay), "MaxDelay must not be negative.");
        Throw.IfOutOfRange(options.MaxDelay > DelayHelper.MaximumDelay, nameof(options.MaxDelay), "MaxDelay exceeds the runtime timer limit.");

        _judge = judge;
        _maxRetries = options.MaxRetries;
        _backoff = options.Backoff;
        _maxDelay = options.MaxDelay;
        _onRetry = options.OnRetry;
        _onRetryAsync = options.OnRetryAsync;
        _delayGenerator = options.DelayGenerator;
        _delayGeneratorAsync = options.DelayGeneratorAsync;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal int MaxRetries => _maxRetries;

    internal Backoff Backoff => _backoff;

    internal TimeSpan? MaxDelay => _maxDelay;

    internal bool HasDelayGenerator => _delayGenerator is not null || _delayGeneratorAsync is not null;

    internal bool HasNotification => _onRetry is not null || _onRetryAsync is not null;

    internal override bool InvokesContinuationAtMostOnce => _maxRetries == 0;

    public override string Describe()
    {
        var cap = _maxDelay is { } max ? $", ≤{DescribeHelper.Time(max)}" : string.Empty;
        return _maxRetries == int.MaxValue
            ? $"RetryForever({_backoff}{cap})"
            : $"Retry({_maxRetries}, {_backoff}{cap})";
    }

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var execution = next.InvokeAsync(context);
        var firstOutcomeShouldRetry = false;
        if (execution.IsCompletedSuccessfully)
        {
            var outcome = execution.Result;
            if (!ShouldRetry(in outcome, retriesUsed: 0, context))
            {
                return new ValueTask<Outcome<T>>(outcome);
            }

            firstOutcomeShouldRetry = true;
        }

        return ExecuteCoreAsync(next, context, execution, firstOutcomeShouldRetry);
    }

    private async ValueTask<Outcome<T>> ExecuteCoreAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        ValueTask<Outcome<T>> execution,
        bool firstOutcomeShouldRetry)
    {
        for (var retriesUsed = 0; ; retriesUsed++)
        {
            var outcome = await execution.ConfigureAwait(false);

            if (!firstOutcomeShouldRetry && !ShouldRetry(in outcome, retriesUsed, context))
            {
                return outcome;
            }

            firstOutcomeShouldRetry = false;

            var attempt = retriesUsed + 1;
            KevlarMetrics.Retry(context.ShieldName);
            var delay = _backoff.GetDelay(attempt);

            if (_maxDelay is { } cap && delay > cap)
            {
                delay = cap;
            }

            if (_delayGenerator is not null
                || _delayGeneratorAsync is not null
                || _onRetry is not null
                || _onRetryAsync is not null)
            {
                var result = outcome.Exception is null ? (object?)outcome.Result : null;

                if (_delayGenerator is not null)
                {
                    var generated = _delayGenerator(
                        new RetryEvent(attempt, delay, outcome.Exception, result, context));
                    delay = ApplyGeneratedDelay(delay, generated);
                }

                if (_delayGeneratorAsync is not null)
                {
                    var generated = await _delayGeneratorAsync(
                        new RetryEvent(attempt, delay, outcome.Exception, result, context))
                        .ConfigureAwait(false);
                    delay = ApplyGeneratedDelay(delay, generated);
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

            execution = next.InvokeAsync(context);
        }
    }

    private bool ShouldRetry<T>(in Outcome<T> outcome, int retriesUsed, KevlarContext context) =>
        retriesUsed < _maxRetries
        && _judge.ShouldHandle(in outcome)
        && !context.CancellationToken.IsCancellationRequested;

    private TimeSpan ApplyGeneratedDelay(TimeSpan current, TimeSpan? generated)
    {
        if (generated is not { } custom || custom < TimeSpan.Zero)
        {
            return current;
        }

        // MaxDelay is an absolute bound: it also caps generator-supplied delays
        // such as a server's Retry-After suggestion.
        return _maxDelay is { } absolute && custom > absolute
            ? absolute
            : DelayHelper.Clamp(custom);
    }
}

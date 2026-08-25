using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class RetryStrategy : Strategy
{
    private readonly OutcomeJudge _judge;
    private readonly int _maxRetries;
    private readonly Backoff _backoff;
    private readonly TimeSpan? _maxDelay;
    private readonly Delegate? _onRetry;
    private readonly Delegate? _onRetryAsync;
    private readonly Delegate? _delayGenerator;
    private readonly Delegate? _delayGeneratorAsync;
    private readonly Type? _callbackResultType;

    public RetryStrategy(RetryOptions options, OutcomeJudge judge)
        : this(
            options.MaxRetries,
            options.Backoff,
            options.MaxDelay,
            judge,
            options.OnRetry,
            options.OnRetryAsync,
            options.DelayGenerator,
            options.DelayGeneratorAsync,
            options.HasHandlingOverride,
            callbackResultType: null,
            optionsType: options.GetType())
    {
    }

    private RetryStrategy(
        int maxRetries,
        Backoff backoff,
        TimeSpan? maxDelay,
        OutcomeJudge judge,
        Delegate? onRetry,
        Delegate? onRetryAsync,
        Delegate? delayGenerator,
        Delegate? delayGeneratorAsync,
        bool hasHandlingOverride,
        Type? callbackResultType,
        Type optionsType)
    {
        ConfigurationValidation.ThrowIf(
            maxRetries < 0,
            optionsType,
            nameof(RetryOptions.MaxRetries),
            maxRetries,
            "must not be negative");
        ConfigurationValidation.ThrowIf(
            backoff is null,
            optionsType,
            nameof(RetryOptions.Backoff),
            backoff,
            "must not be null");
        ConfigurationValidation.ThrowIf(
            maxDelay.HasValue && maxDelay.Value < TimeSpan.Zero,
            optionsType,
            nameof(RetryOptions.MaxDelay),
            maxDelay,
            "must not be negative");
        ConfigurationValidation.ThrowIf(
            maxDelay > DelayHelper.MaximumDelay,
            optionsType,
            nameof(RetryOptions.MaxDelay),
            maxDelay,
            "must not exceed the runtime timer limit");

        _judge = judge;
        _maxRetries = maxRetries;
        _backoff = backoff!;
        _maxDelay = maxDelay;
        _onRetry = onRetry;
        _onRetryAsync = onRetryAsync;
        _delayGenerator = delayGenerator;
        _delayGeneratorAsync = delayGeneratorAsync;
        _callbackResultType = callbackResultType;
        HasHandlingOverride = hasHandlingOverride;
    }

    internal static RetryStrategy Create<TResult>(RetryOptions<TResult> options, OutcomeJudge judge)
    {
        return new RetryStrategy(
            options.MaxRetries,
            options.Backoff,
            options.MaxDelay,
            judge,
            options.OnRetry,
            options.OnRetryAsync,
            options.DelayGenerator,
            options.DelayGeneratorAsync,
            options.HasHandlingOverride,
            typeof(TResult),
            options.GetType());
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    internal int MaxRetries => _maxRetries;

    internal Backoff Backoff => _backoff;

    internal TimeSpan? MaxDelay => _maxDelay;

    internal bool HasDelayGenerator => _delayGenerator is not null || _delayGeneratorAsync is not null;

    internal bool HasNotification => _onRetry is not null || _onRetryAsync is not null;

    protected internal override bool InvokesContinuationAtMostOnce => _maxRetries == 0;

    internal override bool RequiresContinuationOverlapIsolation => false;

    public override string Describe()
    {
        var cap = _maxDelay is { } max ? $", ≤{DescribeHelper.Time(max)}" : string.Empty;
        return _maxRetries == int.MaxValue
            ? $"RetryForever({_backoff}{cap})"
            : $"Retry({_maxRetries}, {_backoff}{cap})";
    }

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var strategyIndex = context.StrategyIndex;
        var execution = next.InvokeAsync(context);
        var firstOutcomeShouldRetry = false;
        if (execution.IsCompletedSuccessfully)
        {
            var outcome = execution.Result;
            if (!ShouldRetry(in outcome, retriesUsed: 0, context, strategyIndex))
            {
                return new ValueTask<Outcome<T>>(outcome);
            }

            execution = new ValueTask<Outcome<T>>(outcome);
            firstOutcomeShouldRetry = true;
        }

        return ExecuteCoreAsync(next, context, execution, firstOutcomeShouldRetry, strategyIndex);
    }

    private async ValueTask<Outcome<T>> ExecuteCoreAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        ValueTask<Outcome<T>> execution,
        bool firstOutcomeShouldRetry,
        int strategyIndex)
    {
        var previousBackoffDelay = TimeSpan.Zero;
        for (var retriesUsed = 0; ; retriesUsed++)
        {
            var outcome = await execution.ConfigureAwait(false);

            if (!firstOutcomeShouldRetry && !ShouldRetry(in outcome, retriesUsed, context, strategyIndex))
            {
                return outcome;
            }

            firstOutcomeShouldRetry = false;

            var attempt = retriesUsed + 1;
            KevlarMetrics.Retry(context.ShieldName);
            var delay = _backoff.GetDelay(attempt, previousBackoffDelay);

            if (_maxDelay is { } cap && delay > cap)
            {
                delay = cap;
            }

            previousBackoffDelay = delay;

            if (_delayGenerator is not null
                || _delayGeneratorAsync is not null
                || _onRetry is not null
                || _onRetryAsync is not null)
            {
                if (_delayGenerator is not null)
                {
                    var generated = InvokeDelayGenerator(
                        _delayGenerator,
                        attempt,
                        delay,
                        in outcome,
                        context);
                    delay = ApplyGeneratedDelay(delay, generated);
                }

                if (_delayGeneratorAsync is not null)
                {
                    var generated = await InvokeDelayGeneratorAsync(
                        _delayGeneratorAsync,
                        attempt,
                        delay,
                        outcome,
                        context)
                        .ConfigureAwait(false);
                    delay = ApplyGeneratedDelay(delay, generated);
                }

                if (_onRetry is not null || _onRetryAsync is not null)
                {
                    if (_onRetry is not null)
                    {
                        try
                        {
                            InvokeOnRetry(_onRetry, attempt, delay, in outcome, context);
                        }
                        catch (Exception exception)
                        {
                            KevlarDiagnostics.ReportCallbackError(CallbackErrorKind.Retry, context, exception);
                        }
                    }

                    if (_onRetryAsync is not null)
                    {
                        try
                        {
                            await InvokeOnRetryAsync(
                                _onRetryAsync,
                                attempt,
                                delay,
                                outcome,
                                context).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            KevlarDiagnostics.ReportCallbackError(CallbackErrorKind.Retry, context, exception);
                        }
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

    private bool ShouldRetry<T>(
        in Outcome<T> outcome,
        int retriesUsed,
        KevlarContext context,
        int strategyIndex) =>
        retriesUsed < _maxRetries
        && !context.Properties.SuppressAdditionalAttempts
        && _judge.ShouldHandle(in outcome, context, retriesUsed, strategyIndex)
        && !context.CancellationToken.IsCancellationRequested;

    private TimeSpan? InvokeDelayGenerator<T>(
        Delegate generator,
        int retryNumber,
        TimeSpan delay,
        in Outcome<T> outcome,
        KevlarContext context)
    {
        if (_callbackResultType is null)
        {
            return ((Func<RetryEvent, TimeSpan?>)generator)(
                CreateUntypedEvent(retryNumber, delay, in outcome, context));
        }

        ValidateCallbackResultType<T>();
        return ((Func<RetryEvent<T>, TimeSpan?>)generator)(
            new RetryEvent<T>(retryNumber, delay, outcome, context));
    }

    private ValueTask<TimeSpan?> InvokeDelayGeneratorAsync<T>(
        Delegate generator,
        int retryNumber,
        TimeSpan delay,
        Outcome<T> outcome,
        KevlarContext context)
    {
        if (_callbackResultType is null)
        {
            return ((Func<RetryEvent, ValueTask<TimeSpan?>>)generator)(
                CreateUntypedEvent(retryNumber, delay, in outcome, context));
        }

        ValidateCallbackResultType<T>();
        return ((Func<RetryEvent<T>, ValueTask<TimeSpan?>>)generator)(
            new RetryEvent<T>(retryNumber, delay, outcome, context));
    }

    private void InvokeOnRetry<T>(
        Delegate callback,
        int retryNumber,
        TimeSpan delay,
        in Outcome<T> outcome,
        KevlarContext context)
    {
        if (_callbackResultType is null)
        {
            ((Action<RetryEvent>)callback)(
                CreateUntypedEvent(retryNumber, delay, in outcome, context));
            return;
        }

        ValidateCallbackResultType<T>();
        ((Action<RetryEvent<T>>)callback)(
            new RetryEvent<T>(retryNumber, delay, outcome, context));
    }

    private ValueTask InvokeOnRetryAsync<T>(
        Delegate callback,
        int retryNumber,
        TimeSpan delay,
        Outcome<T> outcome,
        KevlarContext context)
    {
        if (_callbackResultType is null)
        {
            return ((Func<RetryEvent, ValueTask>)callback)(
                CreateUntypedEvent(retryNumber, delay, in outcome, context));
        }

        ValidateCallbackResultType<T>();
        return ((Func<RetryEvent<T>, ValueTask>)callback)(
            new RetryEvent<T>(retryNumber, delay, outcome, context));
    }

    private static RetryEvent CreateUntypedEvent<T>(
        int retryNumber,
        TimeSpan delay,
        in Outcome<T> outcome,
        KevlarContext context) =>
        new(
            retryNumber,
            delay,
            outcome.Exception,
            outcome.Exception is null ? outcome.Result : null,
            context);

    private void ValidateCallbackResultType<T>()
    {
        if (_callbackResultType != typeof(T))
        {
            throw new InvalidOperationException(
                $"The retry callbacks were created for '{_callbackResultType}', " +
                $"but this execution returns '{typeof(T)}'.");
        }
    }

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

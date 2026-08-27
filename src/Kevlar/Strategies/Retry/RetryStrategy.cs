using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class RetryStrategy : Strategy
{
    private readonly OutcomeJudge _judge;
    private readonly int _maxRetries;
    private readonly Backoff _backoff;
    private readonly TimeSpan? _maxDelay;
    private readonly Delegate? _onRetry;
    private readonly Delegate? _delayGenerator;
    private readonly Type? _callbackResultType;
    private readonly string _telemetryName;
    private readonly string _onRetryHookName;
    private readonly string _delayGeneratorHookName;

    public RetryStrategy(RetryOptions options, OutcomeJudge judge)
        : this(
            options.MaxRetries,
            options.Backoff,
            options.MaxDelay,
            judge,
            options.OnRetry,
            options.DelayGenerator,
            options.Name,
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
        Delegate? delayGenerator,
        string? telemetryName,
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
        _maxDelay = maxDelay ?? _backoff.MaxDelay;
        _onRetry = onRetry;
        _delayGenerator = delayGenerator;
        _callbackResultType = callbackResultType;
        _telemetryName = telemetryName ?? "Retry";
        HasHandlingOverride = hasHandlingOverride;
        var optionsName = callbackResultType is null ? "RetryOptions" : "RetryOptions<TResult>";
        _onRetryHookName = optionsName + "." + nameof(RetryOptions.OnRetry);
        _delayGeneratorHookName = optionsName + "." + nameof(RetryOptions.DelayGenerator);
    }

    internal static RetryStrategy Create<TResult>(RetryOptions<TResult> options, OutcomeJudge judge)
    {
        return new RetryStrategy(
            options.MaxRetries,
            options.Backoff,
            options.MaxDelay,
            judge,
            options.OnRetry,
            options.DelayGenerator,
            options.Name,
            options.HasHandlingOverride,
            typeof(TResult),
            options.GetType());
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    internal int MaxRetries => _maxRetries;

    internal Backoff Backoff => _backoff;

    internal TimeSpan? MaxDelay => _maxDelay;

    internal bool HasDelayGenerator => _delayGenerator is not null;

    internal bool HasNotification => _onRetry is not null;

    protected internal override bool InvokesContinuationAtMostOnce => _maxRetries == 0;

    internal override bool RequiresContinuationOverlapIsolation => false;

    public override string Describe()
    {
        var cap = _maxDelay is { } max && max != _backoff.MaxDelay
            ? $", ≤{DescribeHelper.Time(max)}"
            : string.Empty;
        return _maxRetries == int.MaxValue
            ? $"RetryForever({_backoff}{cap})"
            : $"Retry({_maxRetries}, {_backoff}{cap})";
    }

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var strategyIndex = context.StrategyIndex;
        var previousAttemptNumber = context.AttemptNumber;
        context.AttemptNumber = 0;
        var recordAttempts = KevlarTelemetry.AttemptEnabled;
        var attemptStartedAt = recordAttempts ? context.TimeProvider.GetTimestamp() : 0;
        try
        {
            var execution = next.InvokeAsync(context);
            var firstOutcomeShouldRetry = false;
            if (execution.IsCompletedSuccessfully)
            {
                var outcome = execution.Result;
                RecordAttempt(context, strategyIndex, attempt: 0, attemptStartedAt, recordAttempts, in outcome);
                if (!ShouldRetry(in outcome, retriesUsed: 0, context, strategyIndex))
                {
                    context.AttemptNumber = previousAttemptNumber;
                    return new ValueTask<Outcome<T>>(outcome);
                }

                execution = new ValueTask<Outcome<T>>(outcome);
                firstOutcomeShouldRetry = true;
            }

            return ExecuteCoreAsync(
                next,
                context,
                execution,
                firstOutcomeShouldRetry,
                strategyIndex,
                attemptStartedAt,
                recordAttempts,
                previousAttemptNumber);
        }
        catch
        {
            context.AttemptNumber = previousAttemptNumber;
            throw;
        }
    }

    private async ValueTask<Outcome<T>> ExecuteCoreAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        ValueTask<Outcome<T>> execution,
        bool firstOutcomeShouldRetry,
        int strategyIndex,
        long attemptStartedAt,
        bool recordAttempts,
        int previousAttemptNumber)
    {
        try
        {
            var previousBackoffDelay = TimeSpan.Zero;
            for (var retriesUsed = 0; ; retriesUsed++)
            {
                var outcome = await execution.ConfigureAwait(false);
                if (!firstOutcomeShouldRetry)
                {
                    RecordAttempt(
                        context,
                        strategyIndex,
                        retriesUsed,
                        attemptStartedAt,
                        recordAttempts,
                        in outcome);
                }

                if (!firstOutcomeShouldRetry && !ShouldRetry(in outcome, retriesUsed, context, strategyIndex))
                {
                    return outcome;
                }

                firstOutcomeShouldRetry = false;

                var attempt = retriesUsed + 1;
                KevlarMetrics.Retry(context);
                var delay = _backoff.GetDelay(attempt, previousBackoffDelay);

                if (_maxDelay is { } cap && delay > cap)
                {
                    delay = cap;
                }

                previousBackoffDelay = delay;

                if (_delayGenerator is not null)
                {
                    var generated = await InvokeDelayGeneratorAsync(
                        _delayGenerator,
                        retriesUsed,
                        delay,
                        outcome,
                        context)
                        .ConfigureAwait(false);
                    delay = ApplyGeneratedDelay(delay, generated);
                }

                if (KevlarTelemetry.IsEventEnabled(context))
                {
                    KevlarTelemetry.RecordResult(
                        context,
                        strategyName: _telemetryName,
                        eventName: "retry",
                        KevlarTelemetrySeverity.Warning,
                        strategyIndex,
                        attempt,
                        in outcome,
                        delay: delay);
                }

                if (_onRetry is not null)
                {
                    await InvokeOnRetryAsync(
                        _onRetry,
                        retriesUsed,
                        delay,
                        outcome,
                        context).ConfigureAwait(false);
                }

                await OutcomeDisposer.DisposeResultAsync(in outcome, context).ConfigureAwait(false);

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

                attemptStartedAt = recordAttempts ? context.TimeProvider.GetTimestamp() : 0;
                context.AttemptNumber = attempt;
                execution = next.InvokeAsync(context);
            }
        }
        finally
        {
            context.AttemptNumber = previousAttemptNumber;
        }
    }

    private void RecordAttempt<T>(
        KevlarContext context,
        int strategyIndex,
        int attempt,
        long startedAt,
        bool enabled,
        in Outcome<T> outcome)
    {
        if (!enabled)
        {
            return;
        }

        KevlarTelemetry.Record(
            context,
            strategyName: _telemetryName,
            eventName: "execution_attempt",
            outcome.IsSuccess
                ? KevlarTelemetrySeverity.Information
                : KevlarTelemetrySeverity.Warning,
            strategyIndex,
            attempt,
            outcome.IsSuccess,
            outcome.Exception,
            context.TimeProvider.GetElapsedTime(startedAt),
            recordAttemptDuration: true);
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

    private ValueTask<TimeSpan?> InvokeDelayGeneratorAsync<T>(
        Delegate generator,
        int attemptNumber,
        TimeSpan delay,
        Outcome<T> outcome,
        KevlarContext context)
    {
        if (_callbackResultType is null)
        {
            return CallbackInvoker.InvokeGenerator(
                (Func<RetryEvent, ValueTask<TimeSpan?>>)generator,
                CreateUntypedEvent(attemptNumber, delay, in outcome, context),
                context,
                _delayGeneratorHookName);
        }

        ValidateCallbackResultType<T>();
        return CallbackInvoker.InvokeGenerator(
            (Func<RetryEvent<T>, ValueTask<TimeSpan?>>)generator,
            new RetryEvent<T>(attemptNumber, delay, outcome, context),
            context,
            _delayGeneratorHookName);
    }

    private ValueTask InvokeOnRetryAsync<T>(
        Delegate callback,
        int attemptNumber,
        TimeSpan delay,
        Outcome<T> outcome,
        KevlarContext context)
    {
        if (_callbackResultType is null)
        {
            return CallbackInvoker.InvokeAsync(
                (Func<RetryEvent, ValueTask>)callback,
                CreateUntypedEvent(attemptNumber, delay, in outcome, context),
                CallbackErrorKind.Retry,
                context,
                _onRetryHookName);
        }

        ValidateCallbackResultType<T>();
        return CallbackInvoker.InvokeAsync(
            (Func<RetryEvent<T>, ValueTask>)callback,
            new RetryEvent<T>(attemptNumber, delay, outcome, context),
            CallbackErrorKind.Retry,
            context,
            _onRetryHookName);
    }

    private static RetryEvent CreateUntypedEvent<T>(
        int attemptNumber,
        TimeSpan delay,
        in Outcome<T> outcome,
        KevlarContext context) =>
        new(
            attemptNumber,
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

namespace Kevlar.Extensions.Http;

/// <summary>Building blocks for HTTP resilience shields.</summary>
public static class HttpShield
{
    /// <summary>
    /// Returns <see langword="true"/> for responses worth retrying: 5xx, 408 Request Timeout
    /// and 429 Too Many Requests.
    /// </summary>
    public static bool IsTransient(HttpResponseMessage response) =>
        response is not null
        && ((int)response.StatusCode is >= 500 and <= 599
            || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout
            || (int)response.StatusCode == 429);

    /// <summary>
    /// Starts a shield that handles the usual transient HTTP failures:
    /// <see cref="HttpRequestException"/>, Kevlar attempt timeouts, 5xx, 408 and 429.
    /// Chain your strategies onto the returned builder.
    /// </summary>
    public static ShieldBuilder<HttpResponseMessage> WhenTransient() =>
        Shield.For<HttpResponseMessage>()
            .When<HttpRequestException>()
            .Or<TimeoutExceededException>()
            .OrResult(IsTransient);

    /// <summary>
    /// A production-ready pipeline, outermost first: 30s total timeout → 3 retries with
    /// exponential jittered backoff honouring <c>Retry-After</c> headers (retried responses are
    /// disposed) → circuit breaker (50% failure ratio over 30s, minimum 10 calls, 15s break) →
    /// 10s per-attempt timeout.
    /// </summary>
    public static Shield<HttpResponseMessage> Standard() => Standard(new StandardHttpShieldOptions());

    /// <summary>Builds the standard HTTP pipeline from <paramref name="options"/>.</summary>
    public static Shield<HttpResponseMessage> Standard(StandardHttpShieldOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        Validate(options);

        var shield = Shield.Timeout(timeout => Copy(options.TotalTimeout, timeout))
            .For<HttpResponseMessage>()
            .When<HttpRequestException>()
            .Or<TimeoutExceededException>()
            .OrResult(IsTransient)
            .Retry(retry => Copy(options.Retry, retry))
            .CircuitBreaker(circuitBreaker => Copy(options.CircuitBreaker, circuitBreaker));

        if (options.ConcurrencyLimit is { } concurrencyLimit)
        {
            shield = shield.ConcurrencyLimit(target => Copy(concurrencyLimit, target));
        }

        return shield.Timeout(timeout => Copy(options.AttemptTimeout, timeout));
    }

    /// <summary>
    /// A <see cref="RetryOptions{TResult}.DelayGenerator"/> honouring the response's
    /// <c>Retry-After</c> header when present and longer than the computed backoff.
    /// </summary>
    public static TimeSpan? RetryAfter(RetryEvent<HttpResponseMessage> retry)
    {
        if (retry.Outcome.Result is not { } response)
        {
            return null;
        }

        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        TimeSpan? suggested = null;

        if (retryAfter.Delta is { } delta)
        {
            suggested = delta;
        }
        else if (retryAfter.Date is { } date)
        {
            suggested = date - retry.Context.TimeProvider.GetUtcNow();
        }

        return suggested is { } value && value > retry.Delay ? value : null;
    }

    private static void Validate(StandardHttpShieldOptions options)
    {
        ValidateTimeout(options.TotalTimeout, nameof(StandardHttpShieldOptions.TotalTimeout));
        ValidateTimeout(options.AttemptTimeout, nameof(StandardHttpShieldOptions.AttemptTimeout));

        if (options.Retry is null)
        {
            throw new ArgumentException("StandardHttpShieldOptions.Retry cannot be null.", nameof(options));
        }

        if (options.CircuitBreaker is null)
        {
            throw new ArgumentException("StandardHttpShieldOptions.CircuitBreaker cannot be null.", nameof(options));
        }

        if (options.Handler is null)
        {
            throw new ArgumentException("StandardHttpShieldOptions.Handler cannot be null.", nameof(options));
        }
    }

    private static void ValidateTimeout(TimeoutOptions? timeout, string propertyName)
    {
        if (timeout is null)
        {
            throw new ArgumentException($"StandardHttpShieldOptions.{propertyName} cannot be null.", "options");
        }

        if (timeout.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                "options",
                $"StandardHttpShieldOptions.{propertyName}.Timeout must be positive.");
        }
    }

    private static void Copy(TimeoutOptions source, TimeoutOptions target)
    {
        target.Timeout = source.Timeout;
        target.TimeoutGenerator = source.TimeoutGenerator;
        target.OnTimeout = source.OnTimeout;
        target.OnTimeoutAsync = source.OnTimeoutAsync;
    }

    private static void Copy(
        RetryOptions<HttpResponseMessage> source,
        RetryOptions<HttpResponseMessage> target)
    {
        var onRetry = source.OnRetry;
        target.MaxRetries = source.MaxRetries;
        target.Backoff = source.Backoff;
        target.MaxDelay = source.MaxDelay;
        target.DelayGenerator = source.DelayGenerator;
        target.DelayGeneratorAsync = source.DelayGeneratorAsync;
        target.HandlesException = source.HandlesException;
        target.HandlesResult = source.HandlesResult;
        target.OnRetry = retry =>
        {
            retry.Outcome.Result?.Dispose();
            onRetry?.Invoke(retry);
        };
        target.OnRetryAsync = source.OnRetryAsync;
    }

    private static void Copy(CircuitBreakerOptions source, CircuitBreakerOptions target)
    {
        target.ConsecutiveFailures = source.ConsecutiveFailures;
        target.FailureRatio = source.FailureRatio;
        target.MinimumThroughput = source.MinimumThroughput;
        target.SamplingWindow = source.SamplingWindow;
        target.BreakDuration = source.BreakDuration;
        target.BreakDurationGenerator = source.BreakDurationGenerator;
        target.HandlesException = source.HandlesException;
        target.Monitor = source.Monitor;
        target.OnStateChanged = source.OnStateChanged;
        target.OnStateChangedAsync = source.OnStateChangedAsync;
    }

    private static void Copy(ConcurrencyLimitOptions source, ConcurrencyLimitOptions target)
    {
        target.MaxConcurrency = source.MaxConcurrency;
        target.MaxQueue = source.MaxQueue;
    }
}

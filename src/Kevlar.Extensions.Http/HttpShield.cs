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
    /// Returns <see langword="true"/> for transient HTTP exceptions, including an
    /// <see cref="HttpClient.Timeout"/> cancellation when <paramref name="callerCancellationToken"/>
    /// was not cancelled.
    /// </summary>
    public static bool IsTransientException(
        Exception? exception,
        CancellationToken callerCancellationToken) =>
        !callerCancellationToken.IsCancellationRequested
        && (exception is HttpRequestException or TimeoutExceededException
            || exception is TaskCanceledException cancellation
                && IsHttpClientTimeout(cancellation));

    /// <summary>
    /// Starts a shield that handles the usual transient HTTP failures:
    /// <see cref="HttpRequestException"/>, <see cref="HttpClient.Timeout"/> cancellations,
    /// Kevlar attempt timeouts, 5xx, 408 and 429.
    /// Chain your strategies onto the returned builder.
    /// </summary>
    public static ShieldBuilder<HttpResponseMessage> WhenTransient() =>
        WhenTransient(Shield.For<HttpResponseMessage>());

    internal static ShieldBuilder<HttpResponseMessage> WhenTransient(
        Shield<HttpResponseMessage> shield) =>
        shield
            .When<HttpRequestException>()
            .Or<TaskCanceledException>(IsHttpClientTimeout)
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

        var shield = WhenTransient(
                Shield.Timeout(timeout => Copy(options.TotalTimeout, timeout))
                    .For<HttpResponseMessage>())
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

    /// <summary>
    /// Creates a <see cref="RetryOptions{TResult}.DelayGenerator"/> that honours
    /// <c>Retry-After</c> while capping the server suggestion at <paramref name="maxDelay"/>.
    /// The computed backoff is never shortened.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxDelay"/> is negative.
    /// </exception>
    public static Func<RetryEvent<HttpResponseMessage>, TimeSpan?> RetryAfter(TimeSpan maxDelay)
    {
        if (maxDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "maxDelay must not be negative.");
        }

        return retry =>
        {
            var suggested = RetryAfter(retry);
            if (suggested is not { } value || value <= maxDelay)
            {
                return suggested;
            }

            return maxDelay > retry.Delay ? maxDelay : null;
        };
    }

    private static bool IsHttpClientTimeout(TaskCanceledException exception)
    {
        if (exception.InnerException is TimeoutException)
        {
            return true;
        }

#if NETSTANDARD2_0
        return !exception.CancellationToken.CanBeCanceled;
#else
        return false;
#endif
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

    private static void Copy(
        CircuitBreakerOptions<HttpResponseMessage> source,
        CircuitBreakerOptions<HttpResponseMessage> target)
    {
        target.ConsecutiveFailures = source.ConsecutiveFailures;
        target.FailureRatio = source.FailureRatio;
        target.MinimumThroughput = source.MinimumThroughput;
        target.SamplingWindow = source.SamplingWindow;
        target.BreakDuration = source.BreakDuration;
        target.BreakDurationGenerator = source.BreakDurationGenerator;
        target.HandlesException = source.HandlesException;
        target.HandlesResult = source.HandlesResult;
        target.Monitor = source.Monitor;
        target.OnStateChanged = source.OnStateChanged;
        target.OnStateChangedAsync = source.OnStateChangedAsync;
    }

    private static void Copy(ConcurrencyLimitOptions source, ConcurrencyLimitOptions target)
    {
        target.MaxConcurrency = source.MaxConcurrency;
        target.QueueLimit = source.QueueLimit;
    }
}

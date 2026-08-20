namespace Kevlar.Extensions.Http;

/// <summary>Building blocks for HTTP resilience policies.</summary>
public static class HttpKevlar
{
    /// <summary>
    /// Returns <see langword="true"/> for responses worth retrying: 5xx, 408 Request Timeout
    /// and 429 Too Many Requests.
    /// </summary>
    public static bool IsTransient(HttpResponseMessage response) =>
        response is not null
        && ((int)response.StatusCode >= 500
            || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout
            || (int)response.StatusCode == 429);

    /// <summary>
    /// Starts a policy that handles the usual transient HTTP failures:
    /// <see cref="HttpRequestException"/>, Kevlar attempt timeouts, 5xx, 408 and 429.
    /// Chain your strategies onto the returned builder.
    /// </summary>
    public static PolicyBuilder<HttpResponseMessage> HandleTransient() =>
        Policy.For<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutExceededException>()
            .HandleResult(IsTransient);

    /// <summary>
    /// A production-ready pipeline, outermost first: 30s total timeout → 3 retries with
    /// exponential jittered backoff honouring <c>Retry-After</c> headers (retried responses are
    /// disposed) → circuit breaker (50% failure ratio over 30s, minimum 10 calls, 15s break) →
    /// 10s per-attempt timeout.
    /// </summary>
    public static Policy<HttpResponseMessage> StandardPolicy() =>
        Policy.Timeout(TimeSpan.FromSeconds(30))
            .For<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutExceededException>()
            .HandleResult(IsTransient)
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.DelayGenerator = RetryAfter;
                options.OnRetry = static retry => (retry.Result as HttpResponseMessage)?.Dispose();
            })
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 0.5;
                options.MinimumThroughput = 10;
                options.SamplingWindow = TimeSpan.FromSeconds(30);
                options.BreakDuration = TimeSpan.FromSeconds(15);
            })
            .Timeout(TimeSpan.FromSeconds(10));

    /// <summary>
    /// A <see cref="RetryOptions.DelayGenerator"/> honouring the response's <c>Retry-After</c>
    /// header when present and longer than the computed backoff.
    /// </summary>
    public static TimeSpan? RetryAfter(RetryEvent retry)
    {
        if (retry.Result is not HttpResponseMessage response)
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
}

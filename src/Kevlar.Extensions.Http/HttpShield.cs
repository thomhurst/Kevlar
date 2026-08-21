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
            .When<TimeoutExceededException>()
            .WhenResult(IsTransient);

    /// <summary>
    /// A production-ready pipeline, outermost first: 30s total timeout → 3 retries with
    /// exponential jittered backoff honouring <c>Retry-After</c> headers → circuit breaker
    /// (50% failure ratio over 30s, minimum 10 calls, 15s break) →
    /// 10s per-attempt timeout.
    /// </summary>
    public static Shield<HttpResponseMessage> Standard() =>
        Shield.Timeout(TimeSpan.FromSeconds(30))
            .For<HttpResponseMessage>()
            .When<HttpRequestException>()
            .When<TimeoutExceededException>()
            .WhenResult(IsTransient)
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.DelayGenerator = RetryAfter;
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
}

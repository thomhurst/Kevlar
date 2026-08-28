namespace Kevlar.Tests;

public class OptionsValidationTests
{
    [Test]
    public async Task Configuration_Errors_Name_Options_Property_And_Value()
    {
        ConfigurationCase[] cases =
        [
            new(() => Shield.Retry(options => options.MaxRetries = -1), "RetryOptions", "MaxRetries", "-1"),
            new(() => Shield.Retry(options => options.Backoff = null!), "RetryOptions", "Backoff", "null"),
            new(() => Shield.Retry(options => options.MaxDelay = TimeSpan.FromSeconds(-1)), "RetryOptions", "MaxDelay", "-00:00:01"),
            new(() => Shield.Retry(options => options.MaxDelay = TimeSpan.MaxValue), "RetryOptions", "MaxDelay", TimeSpan.MaxValue.ToString()),
            new(() => Shield.Timeout(options => options.Timeout = TimeSpan.Zero), "TimeoutOptions", "Timeout", "00:00:00"),
            new(() => Shield.Timeout(options => options.Timeout = TimeSpan.MaxValue), "TimeoutOptions", "Timeout", TimeSpan.MaxValue.ToString()),
            new(() => Shield.CircuitBreaker(options => options.ConsecutiveFailures = 0), "CircuitBreakerOptions", "ConsecutiveFailures", "0"),
            new(() => Shield.CircuitBreaker(options => options.FailureRatio = double.NaN), "CircuitBreakerOptions", "FailureRatio", "NaN"),
            new(() => Shield.CircuitBreaker(options => options.MinimumThroughput = 0), "CircuitBreakerOptions", "MinimumThroughput", "0"),
            new(() => Shield.CircuitBreaker(options => options.SamplingWindow = TimeSpan.Zero), "CircuitBreakerOptions", "SamplingWindow", "00:00:00"),
            new(() => Shield.CircuitBreaker(options => options.BreakDuration = TimeSpan.Zero), "CircuitBreakerOptions", "BreakDuration", "00:00:00"),
            new(() => Shield.Hedge(options => options.MaxHedgedAttempts = -1), "HedgeOptions", "MaxHedgedAttempts", "-1"),
            new(() => Shield.RateLimit(options => options.Permits = 0), "RateLimitOptions", "Permits", "0"),
            new(() => Shield.RateLimit(options => options.Window = TimeSpan.Zero), "RateLimitOptions", "Window", "00:00:00"),
            new(() => Shield.RateLimit(options => options.Burst = 0), "RateLimitOptions", "Burst", "0"),
            new(() => Shield.RateLimit(options => options.QueueLimit = -1), "RateLimitOptions", "QueueLimit", "-1"),
            new(() => Shield.ConcurrencyLimit(options => options.MaxConcurrency = 0), "ConcurrencyLimitOptions", "MaxConcurrency", "0"),
            new(() => Shield.ConcurrencyLimit(options => options.QueueLimit = -1), "ConcurrencyLimitOptions", "QueueLimit", "-1"),
        ];

        foreach (var item in cases)
        {
            var exception = await Assert.That(item.Build).Throws<KevlarConfigurationException>();
            await Assert.That(exception!.Message).Contains(item.OptionsType);
            await Assert.That(exception.Message).Contains(item.Property);
            await Assert.That(exception.Message).Contains(item.Value);
            await Assert.That(exception is KevlarException).IsTrue();
        }
    }

    [Test]
    public async Task Direct_Argument_Errors_Keep_Correct_ParamName()
    {
        DirectArgumentCase[] cases =
        [
            new(() => Shield.Retry(-1), typeof(Shield), nameof(Shield.Retry), [typeof(int)]),
            new(() => Shield.Timeout(TimeSpan.Zero), typeof(Shield), nameof(Shield.Timeout), [typeof(TimeSpan)]),
            new(() => Shield.CircuitBreaker(0, TimeSpan.FromSeconds(1)), typeof(Shield), nameof(Shield.CircuitBreaker), [typeof(int), typeof(TimeSpan)]),
            new(() => Shield.RateLimit(0, TimeSpan.FromSeconds(1)), typeof(Shield), nameof(Shield.RateLimit), [typeof(int), typeof(TimeSpan)]),
            new(() => Shield.ConcurrencyLimit(0), typeof(Shield), nameof(Shield.ConcurrencyLimit), [typeof(int), typeof(int)]),
            new(() => Shield.Hedge(-1, TimeSpan.Zero), typeof(Shield), nameof(Shield.Hedge), [typeof(int), typeof(TimeSpan)]),
            new(() => Shield.For<int>().Retry(-1), typeof(Shield<int>), nameof(Shield<int>.Retry), [typeof(int)]),
            new(() => Shield.When<InvalidOperationException>().Retry(-1), typeof(ShieldBuilder), nameof(ShieldBuilder.Retry), [typeof(int)]),
        ];

        foreach (var item in cases)
        {
            var exception = await Assert.That(item.Invoke).Throws<ArgumentOutOfRangeException>();
            var method = item.DeclaringType.GetMethod(item.MethodName, item.ParameterTypes)!;
            await Assert.That(method.GetParameters().Select(static parameter => parameter.Name))
                .Contains(exception!.ParamName);
        }
    }

    [Test]
    public async Task Null_Fallback_Delegate_Reports_The_Public_Parameter()
    {
        var exception = await Assert.That(() => Shield.Fallback(
                (Func<CancellationToken, ValueTask>)null!))
            .Throws<ArgumentNullException>();

        await Assert.That(exception!.ParamName).IsEqualTo("fallback");
    }

    [Test]
    public async Task Typed_And_Builder_Configuration_Overloads_Have_Parity()
    {
        Action[] invalidConfigurations =
        [
            () => Shield.Retry(options => options.MaxRetries = -1),
            () => Shield.For<int>().Retry(options => options.MaxRetries = -1),
            () => Shield.When<InvalidOperationException>().Retry(options => options.MaxRetries = -1),
            () => Shield.For<int>().When<InvalidOperationException>().Retry(options => options.MaxRetries = -1),
        ];

        foreach (var invalidConfiguration in invalidConfigurations)
        {
            var exception = await Assert.That(invalidConfiguration).Throws<KevlarConfigurationException>();
            await Assert.That(exception!.Message).Contains("RetryOptions");
            await Assert.That(exception.Message).Contains("MaxRetries");
            await Assert.That(exception.Message).Contains("-1");
        }
    }

    [Test]
    public async Task Retry_Of_Zero_Is_Allowed_And_Means_No_Retries()
    {
        var attempts = 0;
        var shield = Shield.Retry(0, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Timeout_Rejects_NonPositive_Durations()
    {
        await Assert.That(() => Shield.Timeout(TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.Timeout(TimeSpan.FromSeconds(-1))).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CircuitBreaker_Rejects_Invalid_Options()
    {
        await Assert.That(() => Shield.CircuitBreaker(0, TimeSpan.FromSeconds(1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.CircuitBreaker(-3, TimeSpan.FromSeconds(1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.CircuitBreaker(1, TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.CircuitBreaker(options => options.FailureRatio = 0)).Throws<KevlarConfigurationException>();
        await Assert.That(() => Shield.CircuitBreaker(options => options.FailureRatio = 1.5)).Throws<KevlarConfigurationException>();
        await Assert.That(() => Shield.CircuitBreaker(options => options.MinimumThroughput = 0)).Throws<KevlarConfigurationException>();
        await Assert.That(() => Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 0.5;
            options.SamplingWindow = TimeSpan.Zero;
        })).Throws<KevlarConfigurationException>();
    }

    [Test]
    public async Task CircuitBreaker_Rejects_Configuring_Both_Trip_Modes()
    {
        var untyped = await Assert.That(() => Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 3;
            options.FailureRatio = 0.5;
        })).Throws<KevlarConfigurationException>();

        var typed = await Assert.That(() => Shield.For<int>().CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 3;
            options.FailureRatio = 0.5;
        })).Throws<KevlarConfigurationException>();

        foreach (var message in new[] { untyped!.Message, typed!.Message })
        {
            await Assert.That(message).Contains("ConsecutiveFailures");
            await Assert.That(message).Contains("FailureRatio");
            await Assert.That(message).Contains("cannot both be set");
            await Assert.That(message).Contains("Clear ConsecutiveFailures");
            await Assert.That(message).Contains("clear FailureRatio");
        }
    }

    [Test]
    public async Task CircuitBreaker_With_Neither_Trip_Mode_Set_Trips_After_Five_Consecutive_Failures()
    {
        await Assert.That(Shield.CircuitBreaker(static _ => { }).ToString())
            .IsEqualTo("CircuitBreaker(5 consecutive, break 15s)");
        await Assert.That(Shield.For<int>().CircuitBreaker(static _ => { }).ToString())
            .IsEqualTo("CircuitBreaker(5 consecutive, break 15s)");
    }

    [Test]
    public async Task RateLimit_Rejects_Invalid_Options()
    {
        await Assert.That(() => Shield.RateLimit(0, TimeSpan.FromSeconds(1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.RateLimit(10, TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.RateLimit(options => options.Burst = 0)).Throws<KevlarConfigurationException>();
        await Assert.That(() => Shield.RateLimit(options => options.QueueLimit = -1)).Throws<KevlarConfigurationException>();
    }

    [Test]
    public async Task Bulkhead_Rejects_Invalid_Options()
    {
        await Assert.That(() => Shield.ConcurrencyLimit(0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.ConcurrencyLimit(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.ConcurrencyLimit(1, -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Hedge_Rejects_Negative_Attempt_Count()
    {
        await Assert.That(() => Shield.Hedge(-1, TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Hedge_Accepts_The_Infinite_Delay_Sentinel()
    {
        var shield = Shield.Hedge(1, System.Threading.Timeout.InfiniteTimeSpan);
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task Dynamic_Generators_Report_Their_Configuration_Property()
    {
        var timeout = Shield.Timeout(options =>
            options.TimeoutGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.Zero));
        var timeoutException = await Assert.That(
            async () => await timeout.ExecuteAsync<int>(static _ => new ValueTask<int>(42)))
            .Throws<KevlarConfigurationException>();

        await Assert.That(timeoutException!.Message).Contains("TimeoutOptions.TimeoutGenerator");
        await Assert.That(timeoutException.Message).Contains("00:00:00");

        var breaker = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDurationGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.Zero);
        });
        var breakerException = await Assert.That(
            async () => await breaker.ExecuteAsync<int>(static _ =>
                throw new InvalidOperationException("operation")))
            .Throws<KevlarConfigurationException>();

        await Assert.That(breakerException!.Message)
            .Contains("CircuitBreakerOptions.BreakDurationGenerator");
        await Assert.That(breakerException.Message).Contains("00:00:00");
    }

    [Test]
    public async Task Execution_Methods_Reject_Null_Actions()
    {
        var shield = Shield.Retry(1, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>((Func<CancellationToken, ValueTask<int>>)null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await shield.ExecuteAsync((Func<CancellationToken, ValueTask>)null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await shield.ExecuteOutcomeAsync<int>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => shield.Execute<int>((Func<CancellationToken, int>)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => shield.Execute((Action<CancellationToken>)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Composition_Methods_Reject_Null_Arguments()
    {
        var shield = Shield.Retry(1, Backoff.None);

        await Assert.That(() => Shield.Compose(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.Compose(shield, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => shield.Wrap((Shield)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => shield.Wrap((Shield<int>)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => shield.Use((Strategy)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => shield.WithName(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => shield.WithTimeProvider(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Handling_Clauses_Reject_Null_Predicates()
    {
        await Assert.That(() => Shield.When<InvalidOperationException>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.When(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.When<InvalidOperationException>().Or<ArgumentException>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.For<int>().WhenResult((Func<int, bool>)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Unbound_Monitor_Throws_A_Helpful_Error()
    {
        var monitor = new CircuitBreakerMonitor();

        var exception = await Assert.That(() => monitor.State).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo(
            "This CircuitBreakerMonitor has not been bound. Assign it to CircuitBreakerOptions.Monitor when building the shield.");
        await Assert.That(() => monitor.Isolate()).Throws<InvalidOperationException>();
        await Assert.That(() => monitor.Reset()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Typed_Handling_Overrides_Report_Each_Predicate_Independently()
    {
        var retry = new RetryOptions<int>();
        var breaker = new CircuitBreakerOptions<int>();
        var hedge = new HedgeOptions<int>();

        await Assert.That(retry.HasHandlingOverride).IsFalse();
        await Assert.That(breaker.HasHandlingOverride).IsFalse();
        await Assert.That(hedge.HasHandlingOverride).IsFalse();

        retry.HandlesException = static _ => true;
        breaker.HandlesException = static _ => true;
        hedge.HandlesException = static _ => true;

        await Assert.That(retry.HasHandlingOverride).IsTrue();
        await Assert.That(breaker.HasHandlingOverride).IsTrue();
        await Assert.That(hedge.HasHandlingOverride).IsTrue();

        retry.HandlesException = null;
        breaker.HandlesException = null;
        hedge.HandlesException = null;
        retry.HandlesResult = static _ => true;
        breaker.HandlesResult = static _ => true;
        hedge.HandlesResult = static _ => true;

        await Assert.That(retry.HasHandlingOverride).IsTrue();
        await Assert.That(breaker.HasHandlingOverride).IsTrue();
        await Assert.That(hedge.HasHandlingOverride).IsTrue();
    }

    private sealed record ConfigurationCase(
        Action Build,
        string OptionsType,
        string Property,
        string Value);

    private sealed record DirectArgumentCase(
        Action Invoke,
        Type DeclaringType,
        string MethodName,
        Type[] ParameterTypes);
}

namespace Kevlar.Tests;

public class OptionsValidationTests
{
    [Test]
    public async Task Retry_Rejects_Invalid_Options()
    {
        await Assert.That(() => Policy.Retry(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.Retry(options => options.Backoff = null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Retry_Of_Zero_Is_Allowed_And_Means_No_Retries()
    {
        var attempts = 0;
        var policy = Policy.Retry(0, Backoff.None);

        await Assert.That(async () => await policy.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Timeout_Rejects_NonPositive_Durations()
    {
        await Assert.That(() => Policy.Timeout(TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.Timeout(TimeSpan.FromSeconds(-1))).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CircuitBreaker_Rejects_Invalid_Options()
    {
        await Assert.That(() => Policy.CircuitBreaker(0, TimeSpan.FromSeconds(1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.CircuitBreaker(-3, TimeSpan.FromSeconds(1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.CircuitBreaker(1, TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.CircuitBreaker(options => options.FailureRatio = 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.CircuitBreaker(options => options.FailureRatio = 1.5)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.CircuitBreaker(options => options.MinimumThroughput = 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.CircuitBreaker(options =>
        {
            options.FailureRatio = 0.5;
            options.SamplingWindow = TimeSpan.Zero;
        })).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CircuitBreaker_Rejects_Configuring_Both_Trip_Modes()
    {
        await Assert.That(() => Policy.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 3;
            options.FailureRatio = 0.5;
        })).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RateLimit_Rejects_Invalid_Options()
    {
        await Assert.That(() => Policy.RateLimit(0, TimeSpan.FromSeconds(1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.RateLimit(10, TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.RateLimit(options => options.Burst = 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.RateLimit(options => options.QueueLimit = -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Bulkhead_Rejects_Invalid_Options()
    {
        await Assert.That(() => Policy.Bulkhead(0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.Bulkhead(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.Bulkhead(1, -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Hedge_Rejects_Invalid_Options()
    {
        await Assert.That(() => Policy.Hedge(0, TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Policy.Hedge(2, TimeSpan.FromSeconds(-5))).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Hedge_Accepts_The_Infinite_Delay_Sentinel()
    {
        var policy = Policy.Hedge(2, System.Threading.Timeout.InfiniteTimeSpan);
        var result = await policy.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task Execution_Methods_Reject_Null_Actions()
    {
        var policy = Policy.Retry(1, Backoff.None);

        await Assert.That(async () => await policy.ExecuteAsync<int>((Func<CancellationToken, ValueTask<int>>)null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await policy.ExecuteAsync((Func<CancellationToken, ValueTask>)null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await policy.ExecuteOutcomeAsync<int>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => policy.Execute<int>((Func<CancellationToken, int>)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => policy.Execute((Action<CancellationToken>)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Composition_Methods_Reject_Null_Arguments()
    {
        var policy = Policy.Retry(1, Backoff.None);

        await Assert.That(() => Policy.Compose(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Policy.Compose(policy, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => policy.Wrap((Policy)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => policy.Wrap((Policy<int>)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => policy.Use(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => policy.WithName(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => policy.WithTimeProvider(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Handling_Clauses_Reject_Null_Predicates()
    {
        await Assert.That(() => Policy.Handle<InvalidOperationException>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Policy.HandleWhen(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Policy.Handle<InvalidOperationException>().Or<ArgumentException>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Policy.For<int>().HandleResult((Func<int, bool>)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Monitor_Cannot_Be_Bound_Twice()
    {
        var monitor = new CircuitBreakerMonitor();
        _ = Policy.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        });

        await Assert.That(() => Policy.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        })).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Unbound_Monitor_Throws_A_Helpful_Error()
    {
        var monitor = new CircuitBreakerMonitor();

        await Assert.That(() => monitor.State).Throws<InvalidOperationException>();
        await Assert.That(() => monitor.Isolate()).Throws<InvalidOperationException>();
        await Assert.That(() => monitor.Reset()).Throws<InvalidOperationException>();
    }
}

namespace Kevlar.Tests;

public class OptionsValidationTests
{
    [Test]
    public async Task Retry_Rejects_Invalid_Options()
    {
        await Assert.That(() => Shield.Retry(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.Retry(options => options.Backoff = null!)).Throws<ArgumentNullException>();
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
        await Assert.That(() => Shield.CircuitBreaker(options => options.FailureRatio = 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.CircuitBreaker(options => options.FailureRatio = 1.5)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.CircuitBreaker(options => options.MinimumThroughput = 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 0.5;
            options.SamplingWindow = TimeSpan.Zero;
        })).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task CircuitBreaker_Rejects_Configuring_Both_Trip_Modes()
    {
        var untyped = await Assert.That(() => Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 3;
            options.FailureRatio = 0.5;
        })).Throws<ArgumentOutOfRangeException>();

        var typed = await Assert.That(() => Shield.For<int>().CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 3;
            options.FailureRatio = 0.5;
        })).Throws<ArgumentOutOfRangeException>();

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
        await Assert.That(() => Shield.RateLimit(options => options.Burst = 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.RateLimit(options => options.QueueLimit = -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Bulkhead_Rejects_Invalid_Options()
    {
        await Assert.That(() => Shield.ConcurrencyLimit(0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.ConcurrencyLimit(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.ConcurrencyLimit(1, -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Hedge_Rejects_Invalid_Options()
    {
        await Assert.That(() => Shield.Hedge(0, TimeSpan.Zero)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Shield.Hedge(2, TimeSpan.FromSeconds(-5))).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Hedge_Accepts_The_Infinite_Delay_Sentinel()
    {
        var shield = Shield.Hedge(2, System.Threading.Timeout.InfiniteTimeSpan);
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(result).IsEqualTo(1);
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
    public async Task Monitor_Cannot_Be_Bound_Twice()
    {
        var monitor = new CircuitBreakerMonitor();
        _ = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        });

        await Assert.That(() => Shield.CircuitBreaker(options =>
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

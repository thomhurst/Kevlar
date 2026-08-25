using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class NumericBoundaryTests
{
    private static readonly TimeSpan MaximumRuntimeDelay = Kevlar.Internal.DelayHelper.MaximumDelay;

    [Test]
    public async Task Backoff_Factories_Reject_Invalid_Caps_And_Factors()
    {
        await AssertOutOfRangeAsync(
            () => Backoff.Linear(TimeSpan.FromSeconds(1), TimeSpan.FromTicks(-1)),
            "maxDelay");
        await AssertOutOfRangeAsync(
            () => Backoff.Exponential(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromTicks(-1)),
            "maxDelay");
        await AssertOutOfRangeAsync(
            () => Backoff.Constant(TimeSpan.MaxValue),
            "delay");
        await AssertOutOfRangeAsync(
            () => Backoff.Linear(TimeSpan.FromSeconds(1), TimeSpan.MaxValue),
            "maxDelay");
        await AssertOutOfRangeAsync(
            () => Backoff.Exponential(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.MaxValue),
            "maxDelay");

        foreach (var factor in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            await AssertOutOfRangeAsync(
                () => Backoff.Exponential(TimeSpan.FromSeconds(1), factor),
                "factor");
        }
    }

    [Test]
    [Arguments(BackoffKind.None)]
    [Arguments(BackoffKind.Constant)]
    [Arguments(BackoffKind.Linear)]
    [Arguments(BackoffKind.Exponential)]
    [Arguments(BackoffKind.Custom)]
    public async Task Every_Backoff_Rejects_NonPositive_Attempts(BackoffKind kind)
    {
        var backoff = kind switch
        {
            BackoffKind.None => Backoff.None,
            BackoffKind.Constant => Backoff.Constant(TimeSpan.Zero),
            BackoffKind.Linear => Backoff.Linear(TimeSpan.FromTicks(1)),
            BackoffKind.Exponential => Backoff.Exponential(TimeSpan.FromTicks(1), jitter: Jitter.None),
            BackoffKind.Custom => Backoff.Custom(_ => TimeSpan.Zero),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        await AssertOutOfRangeAsync(() => backoff.GetDelay(0), "attempt");
        await AssertOutOfRangeAsync(() => backoff.GetDelay(-1), "attempt");
    }

    [Test]
    public async Task Extreme_Backoff_Inputs_Clamp_Without_Overflow()
    {
        var cap = TimeSpan.FromSeconds(2);
        var linear = Backoff.Linear(TimeSpan.MaxValue, cap);
        var exponential = Backoff.Exponential(TimeSpan.MaxValue, double.MaxValue, cap, jitter: Jitter.None);
        var custom = Backoff.Custom(_ => TimeSpan.MaxValue);

        await Assert.That(linear.GetDelay(int.MaxValue)).IsEqualTo(cap);
        await Assert.That(exponential.GetDelay(int.MaxValue)).IsEqualTo(cap);
        await Assert.That(custom.GetDelay(int.MaxValue)).IsEqualTo(MaximumRuntimeDelay);
    }

    [Test]
    public async Task Zero_Exponential_Backoff_Remains_Zero_When_The_Power_Overflows()
    {
        var backoff = Backoff.Exponential(TimeSpan.Zero, double.MaxValue, jitter: Jitter.None);

        await Assert.That(backoff.GetDelay(3)).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Strategies_Reject_Unsupported_Delay_And_Capacity_Values_Early()
    {
        await AssertConfigurationErrorAsync(
            () => Shield.Retry(options => options.MaxDelay = TimeSpan.FromTicks(-1)),
            "RetryOptions.MaxDelay",
            "must not be negative");
        await AssertConfigurationErrorAsync(
            () => Shield.Retry(options => options.MaxDelay = TimeSpan.MaxValue),
            "RetryOptions.MaxDelay",
            "must not exceed the runtime timer limit");
        await AssertOutOfRangeAsync(
            () => Shield.Timeout(TimeSpan.MaxValue),
            "timeout");
        await AssertOutOfRangeAsync(
            () => Shield.Hedge(1, TimeSpan.MaxValue),
            "delay");
    }

    [Test]
    public async Task Concurrency_Capacity_Above_Int_MaxValue_Remains_Usable()
    {
        var shield = Shield.ConcurrencyLimit(int.MaxValue, 1);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Concurrency_Pending_Count_Does_Not_Wrap_At_Int_MaxValue()
    {
        var shield = Shield.ConcurrencyLimit(int.MaxValue);
        var strategy = shield.Strategies.Single();
        var pending = strategy.GetType().GetField(
            "_pending",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        pending.SetValue(strategy, (long)int.MaxValue);

        var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));

        await Assert.That(outcome.Exception).IsTypeOf<ConcurrencyLimitExceededException>();
        await Assert.That((long)pending.GetValue(strategy)!).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task Circuit_Breaker_Rejects_NaN_Ratio()
    {
        await AssertConfigurationErrorAsync(
            () => Shield.CircuitBreaker(options => options.FailureRatio = double.NaN),
            "CircuitBreakerOptions.FailureRatio");
    }

    [Test]
    public async Task Circuit_Break_Duration_Remains_Usable_At_Maximum_Date()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.MaxValue - TimeSpan.FromTicks(1));
        var shield = Shield
            .CircuitBreaker(1, TimeSpan.MaxValue)
            .WithTimeProvider(timeProvider);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        var rejection = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(42)))
            .Throws<CircuitOpenException>();
        await Assert.That(rejection!.RetryAfter).IsEqualTo(TimeSpan.MaxValue);

        timeProvider.Advance(TimeSpan.FromTicks(1));
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(42)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Maximum_Sampling_Window_Remains_Usable_Near_Maximum_Date()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.MaxValue - TimeSpan.FromTicks(1));
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 1;
                options.MinimumThroughput = 1;
                options.SamplingWindow = TimeSpan.MaxValue;
                options.BreakDuration = TimeSpan.MaxValue;
            })
            .WithTimeProvider(timeProvider);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(42)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Retry_Generator_Delay_Is_Clamped_Before_Callbacks()
    {
        var callbackFailure = new InvalidOperationException("stop before waiting");
        TimeSpan? observedDelay = null;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ => TimeSpan.MaxValue;
            options.OnRetry = retry =>
            {
                observedDelay = retry.Delay;
                throw callbackFailure;
            };
        });

        var caught = await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new ArgumentException()))
            .Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(caught, callbackFailure)).IsTrue();
        await Assert.That(observedDelay).IsEqualTo(MaximumRuntimeDelay);
    }

    [Test]
    public async Task Extreme_Rate_Limit_Window_Returns_A_Valid_Retry_Estimate()
    {
        var shield = Shield
            .RateLimit(1, TimeSpan.MaxValue)
            .WithTimeProvider(new FakeTimeProvider());

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        var rejection = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();

        await Assert.That(rejection!.RetryAfter).IsEqualTo(TimeSpan.MaxValue);

        var longWindowShield = Shield
            .RateLimit(1, TimeSpan.FromDays(100))
            .WithTimeProvider(new FakeTimeProvider());

        await longWindowShield.ExecuteAsync(_ => new ValueTask<int>(1));
        var longWindowRejection = await Assert.That(
                async () => await longWindowShield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();

        await Assert.That(longWindowRejection!.RetryAfter).IsEqualTo(TimeSpan.FromDays(100));
    }

    [Test]
    public async Task Rate_Limit_Preserves_Small_Advances_At_Large_Timestamps()
    {
        var timeProvider = new FixedTimestampTimeProvider(long.MaxValue - 1, timestampFrequency: 1);
        var shield = Shield.RateLimit(1, TimeSpan.FromSeconds(1)).WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        timeProvider.Advance();
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(2));

        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Rate_Limit_Preserves_Timestamp_Rollover()
    {
        var timeProvider = new FixedTimestampTimeProvider(long.MaxValue, timestampFrequency: 1);
        var shield = Shield.RateLimit(1, TimeSpan.FromSeconds(1)).WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        timeProvider.Advance();
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(2));

        await Assert.That(result).IsEqualTo(2);
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(3)))
            .Throws<RateLimitExceededException>();
    }

    private static async Task AssertOutOfRangeAsync(
        Action action,
        string paramName,
        string? expectedMessage = null)
    {
        var exception = await Assert.That(action).Throws<ArgumentOutOfRangeException>();
        await Assert.That(exception!.ParamName).IsEqualTo(paramName);
        if (expectedMessage is not null)
        {
            await Assert.That(exception.Message).Contains(expectedMessage);
        }
    }

    private static async Task AssertConfigurationErrorAsync(
        Action action,
        string expectedProperty,
        string? expectedMessage = null)
    {
        var exception = await Assert.That(action).Throws<KevlarConfigurationException>();
        await Assert.That(exception!.Message).Contains(expectedProperty);
        if (expectedMessage is not null)
        {
            await Assert.That(exception.Message).Contains(expectedMessage);
        }
    }

    private sealed class FixedTimestampTimeProvider(long timestamp, long timestampFrequency) : TimeProvider
    {
        private long _timestamp = timestamp;

        public override long TimestampFrequency => timestampFrequency;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance() => Interlocked.Increment(ref _timestamp);
    }
}

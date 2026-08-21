using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class NumericBoundaryTests
{
    private static readonly TimeSpan MaximumRuntimeDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

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
            BackoffKind.Exponential => Backoff.Exponential(TimeSpan.FromTicks(1), jitter: false),
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
        var exponential = Backoff.Exponential(TimeSpan.MaxValue, double.MaxValue, cap, jitter: false);
        var custom = Backoff.Custom(_ => TimeSpan.MaxValue);

        await Assert.That(linear.GetDelay(int.MaxValue)).IsEqualTo(cap);
        await Assert.That(exponential.GetDelay(int.MaxValue)).IsEqualTo(cap);
        await Assert.That(custom.GetDelay(int.MaxValue)).IsEqualTo(MaximumRuntimeDelay);
    }

    [Test]
    public async Task Strategies_Reject_Unsupported_Delay_And_Capacity_Values_Early()
    {
        await AssertOutOfRangeAsync(
            () => Shield.Retry(options => options.MaxDelay = TimeSpan.FromTicks(-1)),
            "MaxDelay");
        await AssertOutOfRangeAsync(
            () => Shield.Retry(options => options.MaxDelay = TimeSpan.MaxValue),
            "MaxDelay");
        await AssertOutOfRangeAsync(
            () => Shield.Timeout(TimeSpan.MaxValue),
            "Timeout");
        await AssertOutOfRangeAsync(
            () => Shield.Hedge(2, TimeSpan.MaxValue),
            "Delay");
        await AssertOutOfRangeAsync(
            () => Shield.ConcurrencyLimit(int.MaxValue, 1),
            "MaxQueue");
    }

    [Test]
    public async Task Circuit_Breaker_Rejects_NaN_Ratio()
    {
        await AssertOutOfRangeAsync(
            () => Shield.CircuitBreaker(options => options.FailureRatio = double.NaN),
            "FailureRatio");
    }

    [Test]
    public async Task Circuit_Break_Duration_Saturates_At_Maximum_Date()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.MaxValue - TimeSpan.FromTicks(1));
        var shield = Shield
            .CircuitBreaker(1, TimeSpan.MaxValue)
            .WithTimeProvider(timeProvider);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        var rejection = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(42)))
            .Throws<CircuitOpenException>();
        await Assert.That(rejection!.RetryAfter).IsEqualTo(TimeSpan.FromTicks(1));
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

        await Assert.That(rejection!.RetryAfter).IsEqualTo(MaximumRuntimeDelay);
    }

    private static async Task AssertOutOfRangeAsync(Action action, string paramName)
    {
        var exception = await Assert.That(action).Throws<ArgumentOutOfRangeException>();
        await Assert.That(exception!.ParamName).IsEqualTo(paramName);
    }

    public enum BackoffKind
    {
        None,
        Constant,
        Linear,
        Exponential,
        Custom,
    }
}

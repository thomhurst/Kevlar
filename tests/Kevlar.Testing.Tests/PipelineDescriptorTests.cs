using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;
using System.Collections.ObjectModel;

namespace Kevlar.Testing.Tests;

public class PipelineDescriptorTests
{
    [Test]
    public async Task Descriptor_Preserves_Order_Metadata_And_Safe_Configuration()
    {
        var timeProvider = new FakeTimeProvider();
        var shield = Shield
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(2);
                options.OnTimeout = _ => { };
            })
            .Retry(options =>
            {
                options.MaxRetries = 4;
                options.Backoff = Backoff.Constant(TimeSpan.FromMilliseconds(50));
                options.MaxDelay = TimeSpan.FromSeconds(1);
                options.OnRetryAsync = _ => default;
            })
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 0.25;
                options.MinimumThroughput = 8;
                options.SamplingWindow = TimeSpan.FromSeconds(20);
                options.BreakDuration = TimeSpan.FromSeconds(7);
                options.OnStateChanged = _ => { };
            })
            .RateLimit(options =>
            {
                options.Permits = 10;
                options.Window = TimeSpan.FromSeconds(5);
                options.Burst = 12;
                options.QueueLimit = 3;
            })
            .ConcurrencyLimit(6, maxQueue: 2)
            .Hedge(options =>
            {
                options.MaxAttempts = 3;
                options.Delay = TimeSpan.FromMilliseconds(25);
                options.OnHedge = _ => { };
            })
            .WithName("orders")
            .WithTimeProvider(timeProvider);

        var descriptor = shield.GetDescriptor();

        await Assert.That(descriptor.Name).IsEqualTo("orders");
        await Assert.That(descriptor.ResultType).IsNull();
        await Assert.That(descriptor.UsesCustomTimeProvider).IsTrue();
        descriptor.AssertStrategyOrder(
            StrategyKind.Timeout,
            StrategyKind.Retry,
            StrategyKind.CircuitBreaker,
            StrategyKind.RateLimit,
            StrategyKind.ConcurrencyLimit,
            StrategyKind.Hedging);

        var timeout = descriptor.AssertContainsSingle<TimeoutStrategyDescriptor>();
        await Assert.That(timeout.Timeout).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(timeout.HasTimeoutGenerator).IsFalse();
        await Assert.That(timeout.HasNotification).IsTrue();

        var retry = descriptor.AssertContainsSingle<RetryStrategyDescriptor>();
        await Assert.That(retry.MaxRetries).IsEqualTo(4);
        await Assert.That(retry.MaxDelay).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(retry.HasNotification).IsTrue();

        var breaker = descriptor.AssertContainsSingle<CircuitBreakerStrategyDescriptor>();
        await Assert.That(breaker.FailureRatio).IsEqualTo(0.25);
        await Assert.That(breaker.MinimumThroughput).IsEqualTo(8);
        await Assert.That(breaker.SamplingWindow).IsEqualTo(TimeSpan.FromSeconds(20));
        await Assert.That(breaker.BreakDuration).IsEqualTo(TimeSpan.FromSeconds(7));

        var rateLimit = descriptor.AssertContainsSingle<RateLimitStrategyDescriptor>();
        await Assert.That(rateLimit.Permits).IsEqualTo(10);
        await Assert.That(rateLimit.Burst).IsEqualTo(12);
        await Assert.That(rateLimit.QueueLimit).IsEqualTo(3);

        var concurrency = descriptor.AssertContainsSingle<ConcurrencyLimitStrategyDescriptor>();
        await Assert.That(concurrency.MaxConcurrency).IsEqualTo(6);
        await Assert.That(concurrency.MaxQueue).IsEqualTo(2);

        var hedge = descriptor.AssertContainsSingle<HedgingStrategyDescriptor>();
        await Assert.That(hedge.MaxAttempts).IsEqualTo(3);
        await Assert.That(hedge.HasNotification).IsTrue();
    }

    [Test]
    public async Task Typed_Fallback_And_Composition_Are_Described_Without_State_Exposure()
    {
        var outer = Shield.For<int>()
            .WhenResult(-1)
            .Fallback(42)
            .Retry(2, Backoff.None)
            .WithName("typed");
        var composed = outer.Wrap(Shield.Timeout(TimeSpan.FromSeconds(3)));

        var descriptor = composed.GetDescriptor();

        await Assert.That(descriptor.Name).IsEqualTo("typed");
        await Assert.That(descriptor.ResultType).IsEqualTo(typeof(int));
        descriptor.AssertStrategyOrder(
            StrategyKind.Fallback,
            StrategyKind.Retry,
            StrategyKind.Timeout);
        var fallback = descriptor.AssertContainsSingle<FallbackStrategyDescriptor>();
        await Assert.That(fallback.ResultType).IsEqualTo(typeof(int));
        await Assert.That(fallback.IsVoid).IsFalse();
    }

    [Test]
    public async Task Custom_Strategy_Uses_A_Diagnostic_Descriptor()
    {
        var shield = Shield.Empty.Use(new PassThroughStrategy());

        var custom = shield.GetDescriptor().AssertContainsSingle<CustomStrategyDescriptor>();

        await Assert.That(custom.StrategyType).IsEqualTo(typeof(PassThroughStrategy));
        await Assert.That(custom.Description).IsEqualTo("pass-through");
    }

    [Test]
    public async Task Descriptor_Is_An_Immutable_Snapshot_For_Unnamed_Untyped_Shields()
    {
        var shield = Shield.Empty
            .Retry(1, Backoff.None)
            .Retry(2, Backoff.None);

        var descriptor = shield.GetDescriptor();
        var strategies = (IList<StrategyDescriptor>)descriptor.Strategies;

        await Assert.That(descriptor.Name).IsNull();
        await Assert.That(descriptor.ResultType).IsNull();
        await Assert.That(descriptor.UsesCustomTimeProvider).IsFalse();
        await Assert.That(descriptor.Strategies).IsTypeOf<ReadOnlyCollection<StrategyDescriptor>>();
        await Assert.That(strategies.IsReadOnly).IsTrue();
        descriptor.AssertStrategyCount(2);
        await Assert.That(descriptor.AssertContains<RetryStrategyDescriptor>().MaxRetries).IsEqualTo(1);

        var mutation = await Assert.That(() => strategies[0] = strategies[1])
            .Throws<NotSupportedException>();
        await Assert.That(mutation).IsNotNull();
    }

    [Test]
    public async Task Assertion_Failures_Explain_Expected_And_Actual_Shape()
    {
        var descriptor = Shield.Timeout(TimeSpan.FromSeconds(1)).GetDescriptor();

        var orderFailure = await Assert.That(() => descriptor.AssertStrategyOrder(StrategyKind.Retry))
            .Throws<ShieldAssertionException>();
        await Assert.That(orderFailure!.Message).Contains("expected [Retry]");
        await Assert.That(orderFailure.Message).Contains("actual [Timeout]");

        var countFailure = await Assert.That(() => descriptor.AssertStrategyCount(2))
            .Throws<ShieldAssertionException>();
        await Assert.That(countFailure!.Message).Contains("expected 2 strategies, actual 1");

        var presenceFailure = await Assert.That(
                () => descriptor.AssertContains<RetryStrategyDescriptor>())
            .Throws<ShieldAssertionException>();
        await Assert.That(presenceFailure!.Message).Contains("Expected at least one RetryStrategyDescriptor");
    }

    private sealed class PassThroughStrategy : Strategy
    {
        public override string Describe() => "pass-through";

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }
}

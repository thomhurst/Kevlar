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
                options.OnRejectedAsync = _ => default;
            })
            .ConcurrencyLimit(options =>
            {
                options.MaxConcurrency = 6;
                options.QueueLimit = 2;
                options.OnRejected = _ => { };
            })
            .Hedge(options =>
            {
                options.MaxAttempts = 3;
                options.Delay = TimeSpan.FromMilliseconds(25);
                options.OnHedge = _ => { };
                options.ActionGenerator = HedgeActionGenerator.Create(static _ => null);
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
            StrategyKind.Hedge);

        var timeout = descriptor.AssertContainsSingle<TimeoutStrategyDescriptor>();
        await Assert.That(timeout.Timeout).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(timeout.HasTimeoutGenerator).IsFalse();
        await Assert.That(timeout.HasNotification).IsTrue();

        var retry = descriptor.AssertContainsSingle<RetryStrategyDescriptor>();
        await Assert.That(retry.MaxRetries).IsEqualTo(4);
        await Assert.That(retry.MaxDelay).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(retry.HasDelayGenerator).IsFalse();
        await Assert.That(retry.HasNotification).IsTrue();
        await Assert.That(retry.Backoff.Kind).IsEqualTo(BackoffKind.Constant);
        await Assert.That(retry.Backoff.BaseDelay).IsEqualTo(TimeSpan.FromMilliseconds(50));

        var breaker = descriptor.AssertContainsSingle<CircuitBreakerStrategyDescriptor>();
        await Assert.That(breaker.FailureRatio).IsEqualTo(0.25);
        await Assert.That(breaker.MinimumThroughput).IsEqualTo(8);
        await Assert.That(breaker.SamplingWindow).IsEqualTo(TimeSpan.FromSeconds(20));
        await Assert.That(breaker.BreakDuration).IsEqualTo(TimeSpan.FromSeconds(7));
        await Assert.That(breaker.HasMonitor).IsFalse();
        await Assert.That(breaker.HasNotification).IsTrue();

        var rateLimit = descriptor.AssertContainsSingle<RateLimitStrategyDescriptor>();
        await Assert.That(rateLimit.Permits).IsEqualTo(10);
        await Assert.That(rateLimit.Window).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(rateLimit.Burst).IsEqualTo(12);
        await Assert.That(rateLimit.QueueLimit).IsEqualTo(3);
        await Assert.That(rateLimit.HasNotification).IsTrue();

        var concurrency = descriptor.AssertContainsSingle<ConcurrencyLimitStrategyDescriptor>();
        await Assert.That(concurrency.MaxConcurrency).IsEqualTo(6);
        await Assert.That(concurrency.QueueLimit).IsEqualTo(2);
        await Assert.That(concurrency.HasNotification).IsTrue();

        var hedge = descriptor.AssertContainsSingle<HedgeStrategyDescriptor>();
        await Assert.That(hedge.MaxAttempts).IsEqualTo(3);
        await Assert.That(hedge.Delay).IsEqualTo(TimeSpan.FromMilliseconds(25));
        await Assert.That(hedge.HasNotification).IsTrue();
        await Assert.That(hedge.HasActionGenerator).IsTrue();
    }

    [Test]
    public async Task Typed_Action_Generator_Is_Visible_In_The_Descriptor()
    {
        var descriptor = Shield.For<int>()
            .Hedge(options => options.ActionGenerator = static _ => null)
            .GetDescriptor();

        await Assert.That(descriptor.AssertContainsSingle<HedgeStrategyDescriptor>().HasActionGenerator)
            .IsTrue();
    }

    [Test]
    public async Task Typed_Fallback_And_Composition_Are_Described_Without_State_Exposure()
    {
        var outer = Shield.For<int>()
            .WhenResult(-1)
            .FallbackTo(42)
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
        await Assert.That(fallback.HasNotification).IsFalse();

        var configuredFallback = Shield.For<int>()
            .FallbackTo(42, static options => options.OnFallback = static _ => { })
            .GetDescriptor()
            .AssertContainsSingle<FallbackStrategyDescriptor>();
        await Assert.That(configuredFallback.HasNotification).IsTrue();

        var voidFallback = Shield
            .When<InvalidOperationException>()
            .Fallback(static (_, _) => default)
            .GetDescriptor()
            .AssertContainsSingle<FallbackStrategyDescriptor>();
        await Assert.That(voidFallback.ResultType).IsNull();
        await Assert.That(voidFallback.IsVoid).IsTrue();
        await Assert.That(voidFallback.HasNotification).IsFalse();
    }

    [Test]
    public async Task Reactive_Descriptors_Report_Local_Handling_Overrides()
    {
        var descriptor = Shield.Empty
            .Retry(options => options.HandlesException = _ => true)
            .CircuitBreaker(options => options.HandlesException = _ => true)
            .Hedge(options => options.HandlesException = _ => true)
            .Fallback(
                static (_, _) => default,
                options => options.HandlesException = _ => true)
            .GetDescriptor();

        await Assert.That(descriptor.AssertContainsSingle<RetryStrategyDescriptor>().HasHandlingOverride).IsTrue();
        await Assert.That(descriptor.AssertContainsSingle<CircuitBreakerStrategyDescriptor>().HasHandlingOverride).IsTrue();
        await Assert.That(descriptor.AssertContainsSingle<HedgeStrategyDescriptor>().HasHandlingOverride).IsTrue();
        await Assert.That(descriptor.AssertContainsSingle<FallbackStrategyDescriptor>().HasHandlingOverride).IsTrue();

        var ambient = Shield.Retry(1).GetDescriptor().AssertContainsSingle<RetryStrategyDescriptor>();
        await Assert.That(ambient.HasHandlingOverride).IsFalse();
    }

    [Test]
    public async Task Custom_Strategy_Uses_A_Diagnostic_Descriptor()
    {
        var shield = Shield.Empty.Use(new PassThroughStrategy());

        var custom = shield.GetDescriptor().AssertContainsSingle<CustomStrategyDescriptor>();

        await Assert.That(custom.StrategyType).IsEqualTo(typeof(PassThroughStrategy));
        await Assert.That(custom.Description).IsEqualTo("pass-through");
        await Assert.That(custom.Handling).IsNull();
    }

    [Test]
    public async Task Custom_Strategy_Descriptor_Exposes_Declared_Handling()
    {
        var custom = Shield.When<ArgumentException>()
            .Use(clause => new HandlingAwareStrategy(clause))
            .GetDescriptor()
            .AssertContainsSingle<CustomStrategyDescriptor>();

        await Assert.That(custom.Handling.HasValue).IsTrue();
        await Assert.That(custom.Handling!.Value.ShouldHandle(
            Outcome<int>.FromException(new ArgumentException()))).IsTrue();
        await Assert.That(custom.Handling.Value.ShouldHandle(
            Outcome<int>.FromException(new InvalidOperationException()))).IsFalse();
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
    public async Task Custom_Backoff_Is_Described_Without_Executing_Or_Exposing_Callback()
    {
        var callbackCalls = 0;
        var shield = Shield.Retry(1, Backoff.Custom(_ =>
        {
            callbackCalls++;
            return TimeSpan.FromSeconds(callbackCalls);
        }));

        var backoff = shield.GetDescriptor()
            .AssertContainsSingle<RetryStrategyDescriptor>()
            .Backoff;

        await Assert.That(backoff.Kind).IsEqualTo(BackoffKind.Custom);
        await Assert.That(backoff.BaseDelay).IsNull();
        await Assert.That(backoff.Factor).IsNull();
        await Assert.That(backoff.MaxDelay).IsNull();
        await Assert.That(backoff.Jitter).IsNull();
        await Assert.That(callbackCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Built_In_Backoff_Options_Are_Captured_As_Inert_Values()
    {
        var none = DescribeBackoff(Backoff.None);
        var linear = DescribeBackoff(Backoff.Linear(
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(2)));
        var exponential = DescribeBackoff(Backoff.Exponential(
            TimeSpan.FromMilliseconds(30),
            factor: 3,
            maxDelay: TimeSpan.FromSeconds(4),
            jitter: false));

        await Assert.That(none.Kind).IsEqualTo(BackoffKind.None);
        await Assert.That(none.BaseDelay).IsEqualTo(TimeSpan.Zero);
        await Assert.That(linear.Kind).IsEqualTo(BackoffKind.Linear);
        await Assert.That(linear.BaseDelay).IsEqualTo(TimeSpan.FromMilliseconds(20));
        await Assert.That(linear.MaxDelay).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(exponential.Kind).IsEqualTo(BackoffKind.Exponential);
        await Assert.That(exponential.BaseDelay).IsEqualTo(TimeSpan.FromMilliseconds(30));
        await Assert.That(exponential.Factor).IsEqualTo(3);
        await Assert.That(exponential.MaxDelay).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(exponential.Jitter).IsFalse();
    }

    [Test]
    public async Task Retry_Descriptor_Reports_Core_BackoffKind()
    {
        await Assert.That(typeof(BackoffDescriptor).GetProperty(nameof(BackoffDescriptor.Kind))!.PropertyType)
            .IsEqualTo(typeof(BackoffKind));

        var cases = new[]
        {
            Backoff.None,
            Backoff.Constant(TimeSpan.FromMilliseconds(10)),
            Backoff.Linear(TimeSpan.FromMilliseconds(10)),
            Backoff.Exponential(TimeSpan.FromMilliseconds(10), jitter: false),
            Backoff.Custom(_ => TimeSpan.Zero),
        };

        foreach (var backoff in cases)
        {
            await Assert.That(DescribeBackoff(backoff).Kind).IsEqualTo(backoff.Kind);
        }
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

    private sealed class HandlingAwareStrategy(HandlingClause handling) : Strategy
    {
        protected override HandlingClause? Handling => handling;

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private static BackoffDescriptor DescribeBackoff(Backoff backoff) =>
        Shield.Retry(1, backoff)
            .GetDescriptor()
            .AssertContainsSingle<RetryStrategyDescriptor>()
            .Backoff;
}

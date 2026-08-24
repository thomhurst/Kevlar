using Kevlar.Strategies;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class VoidShieldFluentContractTests
{
    [Test]
    public async Task Every_VoidShield_Strategy_Overload_Appends_Exactly_One_Configured_Strategy()
    {
        var baseline = CreateVoidShield();
        var retryConfigured = false;
        var timeoutConfigured = false;
        var breakerConfigured = false;
        var rateConfigured = false;
        var concurrencyConfigured = false;
        var hedgeConfigured = false;

        var cases = new (VoidShield Shield, Type StrategyType)[]
        {
            (baseline.Retry(), typeof(RetryStrategy)),
            (baseline.Retry(2), typeof(RetryStrategy)),
            (baseline.Retry(2, Backoff.None), typeof(RetryStrategy)),
            (baseline.Retry(options =>
            {
                retryConfigured = true;
                options.MaxRetries = 2;
                options.Backoff = Backoff.None;
            }), typeof(RetryStrategy)),
            (baseline.RetryForever(), typeof(RetryStrategy)),
            (baseline.RetryForever(Backoff.None), typeof(RetryStrategy)),
            (baseline.Timeout(TimeSpan.FromSeconds(2)), typeof(TimeoutStrategy)),
            (baseline.Timeout(options =>
            {
                timeoutConfigured = true;
                options.Timeout = TimeSpan.FromSeconds(2);
            }), typeof(TimeoutStrategy)),
            (baseline.CircuitBreaker(2, TimeSpan.FromSeconds(3)), typeof(CircuitBreakerStrategy)),
            (baseline.CircuitBreaker(options =>
            {
                breakerConfigured = true;
                options.ConsecutiveFailures = 2;
                options.BreakDuration = TimeSpan.FromSeconds(3);
            }), typeof(CircuitBreakerStrategy)),
            (baseline.RateLimit(2, TimeSpan.FromSeconds(3)), typeof(RateLimitStrategy)),
            (baseline.RateLimit(options =>
            {
                rateConfigured = true;
                options.Permits = 2;
                options.Window = TimeSpan.FromSeconds(3);
            }), typeof(RateLimitStrategy)),
            (baseline.ConcurrencyLimit(2, 3), typeof(ConcurrencyLimitStrategy)),
            (baseline.ConcurrencyLimit(options =>
            {
                concurrencyConfigured = true;
                options.MaxConcurrency = 2;
                options.QueueLimit = 3;
            }), typeof(ConcurrencyLimitStrategy)),
            (baseline.Hedge(2, TimeSpan.Zero), typeof(HedgingStrategy)),
            (baseline.Hedge(options =>
            {
                hedgeConfigured = true;
                options.MaxAttempts = 2;
                options.Delay = TimeSpan.Zero;
            }), typeof(HedgingStrategy)),
        };

        foreach (var (shield, strategyType) in cases)
        {
            await Assert.That(shield.Strategies.Length).IsEqualTo(baseline.Strategies.Length + 1);
            await Assert.That(shield.Strategies[^1].GetType()).IsEqualTo(strategyType);
            await shield.ExecuteAsync(static _ => ValueTask.CompletedTask);
        }

        await Assert.That(retryConfigured).IsTrue();
        await Assert.That(timeoutConfigured).IsTrue();
        await Assert.That(breakerConfigured).IsTrue();
        await Assert.That(rateConfigured).IsTrue();
        await Assert.That(concurrencyConfigured).IsTrue();
        await Assert.That(hedgeConfigured).IsTrue();
    }

    [Test]
    public async Task VoidShield_Clause_Custom_Fallback_And_Metadata_Forwarding_Preserve_Semantics()
    {
        var baseline = CreateVoidShield();
        var factoryCalls = 0;
        var factorySawClause = false;
        var configuredFallbackCalls = 0;
        var timeProvider = new FakeTimeProvider();

        var fromTypedPredicate = baseline
            .When<InvalidOperationException>(exception => exception.Message == "handled")
            .Retry(1, Backoff.None);
        var fromBarePredicate = baseline
            .When(exception => exception is ArgumentException)
            .Retry(1, Backoff.None);
        var reset = baseline.When<ArgumentException>().Retry(0, Backoff.None).WhenAnyError();
        var custom = baseline.When<ArgumentException>().Use(clause =>
        {
            factoryCalls++;
            factorySawClause = clause.ShouldHandle(
                Outcome<int>.FromException(new ArgumentException()));
            return PassThroughStrategy.Instance;
        });
        var configuredFallback = baseline.Fallback(
            static _ => ValueTask.CompletedTask,
            options =>
            {
                configuredFallbackCalls++;
                options.OnFallback = static _ => { };
            });
        var exceptionFallback = baseline.Fallback(static (_, _) => ValueTask.CompletedTask);
        var exceptionConfiguredFallback = baseline.Fallback(
            static (_, _) => ValueTask.CompletedTask,
            static options => options.OnFallback = static _ => { });
        var namedAndTimed = baseline.WithName("void-contract").WithTimeProvider(timeProvider);
        var wrapped = baseline.Wrap(CreateVoidShield());

        var typedAttempts = 0;
        await fromTypedPredicate.ExecuteAsync(_ =>
        {
            typedAttempts++;
            return typedAttempts == 1
                ? ValueTask.FromException(new InvalidOperationException("handled"))
                : ValueTask.CompletedTask;
        });

        var bareAttempts = 0;
        await fromBarePredicate.ExecuteAsync(_ =>
        {
            bareAttempts++;
            return bareAttempts == 1
                ? ValueTask.FromException(new ArgumentException())
                : ValueTask.CompletedTask;
        });

        await Assert.That(typedAttempts).IsEqualTo(2);
        await Assert.That(bareAttempts).IsEqualTo(2);
        await Assert.That(reset.Name).IsEqualTo(baseline.Name);
        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(factorySawClause).IsTrue();
        await Assert.That(custom.Strategies[^1]).IsSameReferenceAs(PassThroughStrategy.Instance);
        await Assert.That(configuredFallbackCalls).IsEqualTo(1);
        await Assert.That(configuredFallback.Strategies.Length).IsEqualTo(baseline.Strategies.Length + 1);
        await Assert.That(exceptionFallback.Strategies.Length).IsEqualTo(baseline.Strategies.Length + 1);
        await Assert.That(exceptionConfiguredFallback.Strategies.Length).IsEqualTo(baseline.Strategies.Length + 1);
        await Assert.That(namedAndTimed.Name).IsEqualTo("void-contract");
        await Assert.That(namedAndTimed.TimeOrSystem).IsSameReferenceAs(timeProvider);
        await Assert.That(wrapped.Strategies.Length).IsEqualTo(baseline.Strategies.Length * 2);
        await Assert.That(() => baseline.Wrap((VoidShield)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Every_VoidShieldBuilder_Continuation_Preserves_Clause_And_Void_Type()
    {
        var builder = CreateVoidShield().When<InvalidOperationException>();
        var retryConfigured = false;
        var breakerConfigured = false;
        var hedgeConfigured = false;
        var fallbackConfigured = false;
        var rateConfigured = false;
        var concurrencyConfigured = false;
        var factorySawClause = false;

        var cases = new (VoidShield Shield, Type StrategyType)[]
        {
            (builder.Or<ArgumentException>(static exception => exception.ParamName == "value")
                .Retry(0, Backoff.None), typeof(RetryStrategy)),
            (builder.Or(static exception => exception is TimeoutException)
                .Retry(0, Backoff.None), typeof(RetryStrategy)),
            (builder.Retry(), typeof(RetryStrategy)),
            (builder.Retry(2), typeof(RetryStrategy)),
            (builder.Retry(2, Backoff.None), typeof(RetryStrategy)),
            (builder.Retry(options =>
            {
                retryConfigured = true;
                options.MaxRetries = 2;
                options.Backoff = Backoff.None;
            }), typeof(RetryStrategy)),
            (builder.RetryForever(), typeof(RetryStrategy)),
            (builder.RetryForever(Backoff.None), typeof(RetryStrategy)),
            (builder.CircuitBreaker(2, TimeSpan.FromSeconds(3)), typeof(CircuitBreakerStrategy)),
            (builder.CircuitBreaker(options =>
            {
                breakerConfigured = true;
                options.ConsecutiveFailures = 2;
                options.BreakDuration = TimeSpan.FromSeconds(3);
            }), typeof(CircuitBreakerStrategy)),
            (builder.Hedge(2, TimeSpan.Zero), typeof(HedgingStrategy)),
            (builder.Hedge(options =>
            {
                hedgeConfigured = true;
                options.MaxAttempts = 2;
                options.Delay = TimeSpan.Zero;
            }), typeof(HedgingStrategy)),
            (builder.Use(clause =>
            {
                factorySawClause = clause.ShouldHandle(
                    Outcome<int>.FromException(new InvalidOperationException()));
                return PassThroughStrategy.Instance;
            }), typeof(PassThroughStrategy)),
            (builder.Fallback(static (_, _) => ValueTask.CompletedTask), typeof(VoidFallbackStrategy)),
            (builder.Fallback(
                static (_, _) => ValueTask.CompletedTask,
                _ => fallbackConfigured = true), typeof(VoidFallbackStrategy)),
            (builder.Timeout(TimeSpan.FromSeconds(2)), typeof(TimeoutStrategy)),
            (builder.RateLimit(2, TimeSpan.FromSeconds(3)), typeof(RateLimitStrategy)),
            (builder.RateLimit(options =>
            {
                rateConfigured = true;
                options.Permits = 2;
                options.Window = TimeSpan.FromSeconds(3);
            }), typeof(RateLimitStrategy)),
            (builder.ConcurrencyLimit(2, 3), typeof(ConcurrencyLimitStrategy)),
            (builder.ConcurrencyLimit(options =>
            {
                concurrencyConfigured = true;
                options.MaxConcurrency = 2;
                options.QueueLimit = 3;
            }), typeof(ConcurrencyLimitStrategy)),
        };

        foreach (var (shield, strategyType) in cases)
        {
            await Assert.That(shield.Strategies[^1].GetType()).IsEqualTo(strategyType);
            await shield.ExecuteAsync(static _ => ValueTask.CompletedTask);
        }

        await Assert.That(retryConfigured).IsTrue();
        await Assert.That(breakerConfigured).IsTrue();
        await Assert.That(hedgeConfigured).IsTrue();
        await Assert.That(fallbackConfigured).IsTrue();
        await Assert.That(rateConfigured).IsTrue();
        await Assert.That(concurrencyConfigured).IsTrue();
        await Assert.That(factorySawClause).IsTrue();
    }

    private static VoidShield CreateVoidShield() =>
        Shield.Fallback(static _ => ValueTask.CompletedTask);

    private sealed class PassThroughStrategy : Strategy
    {
        public static PassThroughStrategy Instance { get; } = new();

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }
}

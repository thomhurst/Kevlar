using Kevlar.Strategies;

namespace Kevlar.Tests;

public class TypedBuilderForwardingTests
{
    [Test]
    public async Task Typed_Builder_Forwards_Every_Strategy_And_Fallback_Form()
    {
        var builder = Shield.For<int>().WhenResult(-1);
        var retryConfigured = false;
        var breakerConfigured = false;
        var hedgeConfigured = false;
        var fallbackConfigured = 0;
        var rateConfigured = false;
        var concurrencyConfigured = false;

        var cases = new (Shield<int> Shield, Type StrategyType)[]
        {
            (builder.Retry(), typeof(RetryStrategy)),
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
            (builder.Hedge(1, TimeSpan.Zero), typeof(HedgingStrategy)),
            (builder.Hedge(options =>
            {
                hedgeConfigured = true;
                options.MaxHedgedAttempts = 1;
                options.Delay = TimeSpan.Zero;
            }), typeof(HedgingStrategy)),
            (builder.Use(_ => PassThroughStrategy.Instance), typeof(PassThroughStrategy)),
            (builder.FallbackTo(42), typeof(FallbackStrategy<int>)),
            (builder.FallbackTo(42, _ => fallbackConfigured++), typeof(FallbackStrategy<int>)),
            (builder.Fallback(static _ => new ValueTask<int>(42)), typeof(FallbackStrategy<int>)),
            (builder.Fallback(
                static _ => new ValueTask<int>(42),
                _ => fallbackConfigured++), typeof(FallbackStrategy<int>)),
            (builder.Fallback(static (_, _) => new ValueTask<int>(42)), typeof(FallbackStrategy<int>)),
            (builder.Fallback(
                static (_, _) => new ValueTask<int>(42),
                _ => fallbackConfigured++), typeof(FallbackStrategy<int>)),
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
            await Assert.That(shield.Strategies.Length).IsEqualTo(1);
            await Assert.That(shield.Strategies[0].GetType()).IsEqualTo(strategyType);
            await Assert.That(await shield.ExecuteAsync(static _ => new ValueTask<int>(7)))
                .IsEqualTo(7);
        }

        await Assert.That(retryConfigured).IsTrue();
        await Assert.That(breakerConfigured).IsTrue();
        await Assert.That(hedgeConfigured).IsTrue();
        await Assert.That(fallbackConfigured).IsEqualTo(3);
        await Assert.That(rateConfigured).IsTrue();
        await Assert.That(concurrencyConfigured).IsTrue();
    }

    [Test]
    public async Task Multiple_Result_Predicates_Run_In_Order_And_ShortCircuit()
    {
        var calls = new List<string>();
        var shield = Shield.For<int>()
            .WhenResult(value =>
            {
                calls.Add("first");
                return value == 1;
            })
            .OrResult(value =>
            {
                calls.Add("second");
                return value == 2;
            })
            .OrResult(value =>
            {
                calls.Add("third");
                throw new InvalidOperationException($"predicate must not run for {value}");
            })
            .FallbackTo(42);

        var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(2));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(calls).IsEquivalentTo(
            ["first", "second"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Multiple_False_Result_Predicates_Leave_A_Result_Unhandled()
    {
        var calls = new List<int>();
        var shield = Shield.For<int>()
            .WhenResult(value =>
            {
                calls.Add(1);
                return value < 0;
            })
            .OrResult(value =>
            {
                calls.Add(2);
                return value == 0;
            })
            .FallbackTo(42);

        var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(7));

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(calls).IsEquivalentTo(
            [1, 2],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private sealed class PassThroughStrategy : Strategy
    {
        public static PassThroughStrategy Instance { get; } = new();

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }
}

using Kevlar.Strategies;

namespace Kevlar.Tests;

public class StrategyInspectionTests
{
    [Test]
    public async Task Inspection_Metadata_Reflects_Configured_Callbacks()
    {
        var retry = GetStrategy<RetryStrategy>(Shield.Retry(options =>
        {
            options.DelayGeneratorAsync = static _ => default;
            options.OnRetryAsync = static _ => default;
        }));
        var fixedRetry = GetStrategy<RetryStrategy>(Shield.Retry(2, Backoff.None));
        await Assert.That(retry.HasDelayGenerator).IsTrue();
        await Assert.That(retry.HasNotification).IsTrue();
        await Assert.That(fixedRetry.HasDelayGenerator).IsFalse();
        await Assert.That(fixedRetry.HasNotification).IsFalse();

        var timeout = GetStrategy<TimeoutStrategy>(Shield.Timeout(options =>
        {
            options.TimeoutGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
            options.OnTimeoutAsync = static _ => default;
        }));
        var fixedTimeout = GetStrategy<TimeoutStrategy>(Shield.Timeout(TimeSpan.FromSeconds(1)));
        await Assert.That(timeout.HasTimeoutGenerator).IsTrue();
        await Assert.That(timeout.HasNotification).IsTrue();
        await Assert.That(fixedTimeout.HasTimeoutGenerator).IsFalse();
        await Assert.That(fixedTimeout.HasNotification).IsFalse();

        var notifiedHedge = GetStrategy<HedgingStrategy>(Shield.Hedge(options =>
        {
            options.OnHedge = static _ => { };
        }));
        var fixedHedge = GetStrategy<HedgingStrategy>(Shield.Hedge(2, TimeSpan.Zero));
        await Assert.That(notifiedHedge.HasNotification).IsTrue();
        await Assert.That(fixedHedge.HasNotification).IsFalse();

        var monitoredCircuit = GetStrategy<CircuitBreakerStrategy>(Shield.CircuitBreaker(options =>
        {
            options.Monitor = new CircuitBreakerMonitor();
            options.OnStateChanged = static _ => { };
        }));
        var fixedCircuit = GetStrategy<CircuitBreakerStrategy>(
            Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)));
        await Assert.That(monitoredCircuit.Core.HasMonitor).IsTrue();
        await Assert.That(monitoredCircuit.Core.HasNotification).IsTrue();
        await Assert.That(fixedCircuit.Core.HasMonitor).IsFalse();
        await Assert.That(fixedCircuit.Core.HasNotification).IsFalse();

        var notifiedFallback = GetInspection(Shield.For<int>().Fallback(42, static _ => { }));
        var fixedFallback = GetInspection(Shield.For<int>().Fallback(42));
        var voidFallback = GetInspection(
            Shield.When<InvalidOperationException>().Fallback(static (_, _) => default));
        await Assert.That(notifiedFallback.HasNotification).IsTrue();
        await Assert.That(fixedFallback.HasNotification).IsFalse();
        await Assert.That(voidFallback.HasNotification).IsFalse();
        await Assert.That(notifiedFallback.ResultType).IsEqualTo(typeof(int));
        await Assert.That(voidFallback.ResultType).IsNull();
    }

    private static TStrategy GetStrategy<TStrategy>(Shield shield)
        where TStrategy : Strategy => (TStrategy)shield.Strategies.Single();

    private static IFallbackStrategyInspection GetInspection<TResult>(Shield<TResult> shield) =>
        (IFallbackStrategyInspection)shield.Strategies.Single();

    private static IFallbackStrategyInspection GetInspection(Shield shield) =>
        (IFallbackStrategyInspection)shield.Strategies.Single();
}

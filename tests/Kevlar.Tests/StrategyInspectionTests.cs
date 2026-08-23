using Kevlar.Strategies;

namespace Kevlar.Tests;

public class StrategyInspectionTests
{
    [Test]
    public async Task Inspection_Metadata_Reflects_Configured_Callbacks()
    {
        var retryBackoff = Backoff.Linear(TimeSpan.FromMilliseconds(125));
        var retryMaxDelay = TimeSpan.FromSeconds(9);
        var retry = GetStrategy<RetryStrategy>(Shield.Retry(options =>
        {
            options.MaxRetries = 7;
            options.Backoff = retryBackoff;
            options.MaxDelay = retryMaxDelay;
            options.DelayGeneratorAsync = static _ => default;
            options.OnRetryAsync = static _ => default;
        }));
        var fixedRetry = GetStrategy<RetryStrategy>(Shield.Retry(2, Backoff.None));
        await Assert.That(retry.MaxRetries).IsEqualTo(7);
        await Assert.That(retry.Backoff).IsSameReferenceAs(retryBackoff);
        await Assert.That(retry.MaxDelay).IsEqualTo(retryMaxDelay);
        await Assert.That(retry.HasDelayGenerator).IsTrue();
        await Assert.That(retry.HasNotification).IsTrue();
        await Assert.That(fixedRetry.MaxRetries).IsEqualTo(2);
        await Assert.That(fixedRetry.Backoff).IsSameReferenceAs(Backoff.None);
        await Assert.That(fixedRetry.MaxDelay).IsNull();
        await Assert.That(fixedRetry.HasDelayGenerator).IsFalse();
        await Assert.That(fixedRetry.HasNotification).IsFalse();

        var timeoutDuration = TimeSpan.FromSeconds(7);
        var timeout = GetStrategy<TimeoutStrategy>(Shield.Timeout(options =>
        {
            options.Timeout = timeoutDuration;
            options.TimeoutGenerator = static _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
            options.OnTimeoutAsync = static _ => default;
        }));
        var fixedTimeout = GetStrategy<TimeoutStrategy>(Shield.Timeout(TimeSpan.FromSeconds(1)));
        await Assert.That(timeout.Timeout).IsEqualTo(timeoutDuration);
        await Assert.That(timeout.HasTimeoutGenerator).IsTrue();
        await Assert.That(timeout.HasNotification).IsTrue();
        await Assert.That(fixedTimeout.Timeout).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(fixedTimeout.HasTimeoutGenerator).IsFalse();
        await Assert.That(fixedTimeout.HasNotification).IsFalse();

        var notifiedHedge = GetStrategy<HedgingStrategy>(Shield.Hedge(options =>
        {
            options.MaxAttempts = 4;
            options.Delay = TimeSpan.FromMilliseconds(17);
            options.OnHedge = static _ => { };
        }));
        var fixedHedge = GetStrategy<HedgingStrategy>(Shield.Hedge(2, TimeSpan.Zero));
        await Assert.That(notifiedHedge.MaxAttempts).IsEqualTo(4);
        await Assert.That(notifiedHedge.Delay).IsEqualTo(TimeSpan.FromMilliseconds(17));
        await Assert.That(notifiedHedge.HasNotification).IsTrue();
        await Assert.That(fixedHedge.MaxAttempts).IsEqualTo(2);
        await Assert.That(fixedHedge.Delay).IsEqualTo(TimeSpan.Zero);
        await Assert.That(fixedHedge.HasNotification).IsFalse();

        var monitoredCircuit = GetStrategy<CircuitBreakerStrategy>(Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 0.25;
            options.MinimumThroughput = 7;
            options.SamplingWindow = TimeSpan.FromSeconds(13);
            options.BreakDuration = TimeSpan.FromSeconds(19);
            options.Monitor = new CircuitBreakerMonitor();
            options.OnStateChanged = static _ => { };
        }));
        var fixedCircuit = GetStrategy<CircuitBreakerStrategy>(
            Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)));
        await Assert.That(monitoredCircuit.Core.ConsecutiveFailures).IsNull();
        await Assert.That(monitoredCircuit.Core.FailureRatio).IsEqualTo(0.25);
        await Assert.That(monitoredCircuit.Core.MinimumThroughput).IsEqualTo(7);
        await Assert.That(monitoredCircuit.Core.SamplingWindow).IsEqualTo(TimeSpan.FromSeconds(13));
        await Assert.That(monitoredCircuit.Core.BreakDuration).IsEqualTo(TimeSpan.FromSeconds(19));
        await Assert.That(monitoredCircuit.Core.HasMonitor).IsTrue();
        await Assert.That(monitoredCircuit.Core.HasNotification).IsTrue();
        await Assert.That(fixedCircuit.Core.ConsecutiveFailures).IsEqualTo(2);
        await Assert.That(fixedCircuit.Core.FailureRatio).IsNull();
        await Assert.That(fixedCircuit.Core.BreakDuration).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(fixedCircuit.Core.HasMonitor).IsFalse();
        await Assert.That(fixedCircuit.Core.HasNotification).IsFalse();

        var notifiedFallback = GetInspection(Shield.For<int>().Fallback(42, static options => options.OnFallback = static _ => { }));
        var asyncNotifiedFallback = GetInspection(Shield.For<int>().Fallback(
            42,
            static options => options.OnFallbackAsync = static _ => default));
        var fixedFallback = GetInspection(Shield.For<int>().Fallback(42));
        var voidFallback = GetInspection(
            Shield.When<InvalidOperationException>().FallbackAction(static (_, _) => default));
        var notifiedVoidFallback = GetInspection(
            Shield.When<InvalidOperationException>().FallbackAction(
                static (_, _) => default,
                static options => options.OnFallback = static _ => { }));
        var asyncNotifiedVoidFallback = GetInspection(
            Shield.When<InvalidOperationException>().FallbackAction(
                static (_, _) => default,
                static options => options.OnFallbackAsync = static _ => default));
        await Assert.That(notifiedFallback.HasNotification).IsTrue();
        await Assert.That(asyncNotifiedFallback.HasNotification).IsTrue();
        await Assert.That(fixedFallback.HasNotification).IsFalse();
        await Assert.That(voidFallback.HasNotification).IsFalse();
        await Assert.That(notifiedVoidFallback.HasNotification).IsTrue();
        await Assert.That(asyncNotifiedVoidFallback.HasNotification).IsTrue();
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

using System.Reflection;
using Kevlar.Internal;

namespace Kevlar.Tests;

/// <summary>
/// Fails any test that leaves a metric enricher registered.
/// </summary>
/// <remarks>
/// Enrichers are process-wide, and every test that adds one runs under <c>[NotInParallel]</c> and
/// disposes it, so none should remain once a test ends. One that does would add tags to every later
/// test's metrics in the same process, including later runs of the suite when a test host is reused,
/// and fail those tests instead of the one that leaked it. So this check also clears the registry
/// before it fails the test.
/// </remarks>
public static class MetricEnricherLeakCheck
{
    [AfterEvery(HookType.Test)]
    public static void FailOnLeakedEnricher(TestContext context)
    {
        if (!KevlarMetricEnrichment.HasEnrichers)
        {
            return;
        }

        var enrichers = typeof(KevlarMetricEnrichment).GetField("_enrichers", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "A metric enricher is still registered, and KevlarMetricEnrichment._enrichers no longer exists to clear it.");
        enrichers.SetValue(null, Array.Empty<KevlarMetricEnricher>());
        throw new InvalidOperationException(
            "The test left a metric enricher registered. Dispose the subscription that KevlarDiagnostics.AddMetricEnricher returns.");
    }
}

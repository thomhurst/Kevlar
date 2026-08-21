#if NET8_0_OR_GREATER
using System.Diagnostics;
using System.Diagnostics.Metrics;
#endif

namespace Kevlar.Chaos.Internal;

internal static class ChaosMetrics
{
#if NET8_0_OR_GREATER
    private static readonly Meter _meter = new(ChaosDiagnostics.MeterName, "1.0");
    private static readonly Counter<long> _injections = _meter.CreateCounter<long>(
        "kevlar.chaos.injections",
        "{injection}",
        "Chaos injections applied to shield executions.");
#endif

#if NET8_0_OR_GREATER
    public static bool Enabled => _injections.Enabled;
#else
    public static bool Enabled => false;
#endif

    public static void Injection(
        ChaosInjectionKind kind,
        string? shieldName,
        string? operation,
        string? environment)
    {
#if NET8_0_OR_GREATER
        if (!Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { "kevlar.chaos.kind", KindName(kind) },
        };

        if (shieldName is not null)
        {
            tags.Add("kevlar.shield.name", shieldName);
        }

        if (operation is not null)
        {
            tags.Add("kevlar.chaos.operation", operation);
        }

        if (environment is not null)
        {
            tags.Add("kevlar.chaos.environment", environment);
        }

        _injections.Add(1, tags);
#endif
    }

#if NET8_0_OR_GREATER
    private static string KindName(ChaosInjectionKind kind) => kind switch
    {
        ChaosInjectionKind.Latency => "latency",
        ChaosInjectionKind.Fault => "fault",
        ChaosInjectionKind.Outcome => "outcome",
        ChaosInjectionKind.Behavior => "behavior",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
#endif
}

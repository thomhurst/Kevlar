#if NET8_0_OR_GREATER
using System.Diagnostics;
using System.Diagnostics.Metrics;
#endif
using Kevlar.Strategies;

namespace Kevlar.Internal;

/// <summary>
/// Kevlar's built-in instruments. Real counters on .NET 8+ (where
/// <c>System.Diagnostics.Metrics</c> is in-box); no-ops on <c>netstandard2.0</c>.
/// Every recording checks <c>Enabled</c> first, so the
/// cost without a listener is a branch.
/// </summary>
internal static class KevlarMetrics
{
#if NET8_0_OR_GREATER
    private static readonly Meter Meter = new(KevlarDiagnostics.MeterName, "1.0");
    private static readonly Counter<long> Executions = Meter.CreateCounter<long>(
        "kevlar.executions",
        "{execution}",
        "Completed public shield executions.");
    private static readonly Counter<long> Retries = Meter.CreateCounter<long>(
        "kevlar.retries",
        "{retry}",
        "Retry attempts started after the initial attempt.");
    private static readonly Counter<long> Timeouts = Meter.CreateCounter<long>(
        "kevlar.timeouts",
        "{timeout}",
        "Executions cancelled by a timeout strategy.");
    private static readonly Counter<long> Hedges = Meter.CreateCounter<long>(
        "kevlar.hedges",
        "{hedge}",
        "Additional hedged attempts started.");
    private static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>(
        "kevlar.fallbacks",
        "{fallback}",
        "Outcomes replaced by a fallback.");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>(
        "kevlar.rejections",
        "{rejection}",
        "Executions rejected before the user delegate starts.");
    private static readonly Counter<long> CircuitTransitions = Meter.CreateCounter<long>(
        "kevlar.circuit_breaker.transitions",
        "{transition}",
        "Circuit-breaker state transitions.");
#endif

#if NET8_0_OR_GREATER
    public static bool ExecutionEnabled => Executions.Enabled;
#else
    public static bool ExecutionEnabled => false;
#endif

    public static void Execution(string? shieldName, bool success)
    {
#if NET8_0_OR_GREATER
        if (Executions.Enabled)
        {
            var tags = NameTags(shieldName);
            tags.Add("kevlar.execution.outcome", success ? "success" : "failure");
            Executions.Add(1, tags);
        }
#endif
    }

    public static void Retry(string? shieldName)
    {
#if NET8_0_OR_GREATER
        if (Retries.Enabled)
        {
            Retries.Add(1, NameTags(shieldName));
        }
#endif
    }

    public static void Timeout(string? shieldName)
    {
#if NET8_0_OR_GREATER
        if (Timeouts.Enabled)
        {
            Timeouts.Add(1, NameTags(shieldName));
        }
#endif
    }

    public static void Hedge(string? shieldName)
    {
#if NET8_0_OR_GREATER
        if (Hedges.Enabled)
        {
            Hedges.Add(1, NameTags(shieldName));
        }
#endif
    }

    public static void Fallback(string? shieldName)
    {
#if NET8_0_OR_GREATER
        if (Fallbacks.Enabled)
        {
            Fallbacks.Add(1, NameTags(shieldName));
        }
#endif
    }

    public static void Rejection(string? shieldName, string kind)
    {
#if NET8_0_OR_GREATER
        if (Rejections.Enabled)
        {
            var tags = NameTags(shieldName);
            tags.Add("kevlar.rejection.type", kind);
            Rejections.Add(1, tags);
        }
#endif
    }

    public static void CircuitTransition(CircuitState from, CircuitState to)
    {
#if NET8_0_OR_GREATER
        if (CircuitTransitions.Enabled)
        {
            CircuitTransitions.Add(1, new TagList
            {
                { "kevlar.circuit_breaker.state.from", StateName(from) },
                { "kevlar.circuit_breaker.state.to", StateName(to) },
            });
        }
#endif
    }

#if NET8_0_OR_GREATER
    private static TagList NameTags(string? shieldName)
    {
        var tags = default(TagList);
        if (shieldName is not null)
        {
            tags.Add("kevlar.shield.name", shieldName);
        }

        return tags;
    }

    private static string StateName(CircuitState state) => state switch
    {
        CircuitState.Closed => "closed",
        CircuitState.Open => "open",
        CircuitState.HalfOpen => "half_open",
        CircuitState.Isolated => "isolated",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
#endif
}

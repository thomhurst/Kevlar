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
    public const int MaxTrackedStrategyAliases = 64;

#if NET9_0_OR_GREATER
    private const int MinimumCachedStrategyIndex = -1;
    private const int MaximumCachedStrategyIndex = 63;
    private static readonly object[] BoxedStrategyIndexes = CreateBoxedStrategyIndexes();
#endif

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
    private static readonly Histogram<double> ExecutionDuration = Meter.CreateHistogram<double>(
        "kevlar.execution.duration",
        "s",
        "Duration of completed public shield executions.");
#endif

#if NET9_0_OR_GREATER
    private static readonly Gauge<long> CircuitStateGauge = Meter.CreateGauge<long>(
        "kevlar.circuit_breaker.state",
        "{state}",
        "Current circuit-breaker state: closed=0, open=1, half-open=2, isolated=3.");
    private static readonly Gauge<long> ConcurrencyInflight = Meter.CreateGauge<long>(
        "kevlar.concurrency_limit.inflight",
        "{execution}",
        "Executions currently holding a concurrency permit.");
    private static readonly Gauge<long> ConcurrencyQueued = Meter.CreateGauge<long>(
        "kevlar.concurrency_limit.queued",
        "{execution}",
        "Executions currently waiting for a concurrency permit.");
    private static readonly Gauge<long> ConcurrencyCapacity = Meter.CreateGauge<long>(
        "kevlar.concurrency_limit.capacity",
        "{execution}",
        "Configured concurrency permit capacity.");
    private static readonly Gauge<long> RateAvailable = Meter.CreateGauge<long>(
        "kevlar.rate_limit.available",
        "{permit}",
        "Immediately available rate-limit permits.");
    private static readonly Gauge<long> RateQueued = Meter.CreateGauge<long>(
        "kevlar.rate_limit.queued",
        "{execution}",
        "Executions currently waiting for a rate-limit permit.");
#endif

#if NET8_0_OR_GREATER
    public static bool ExecutionEnabled => Executions.Enabled;
#else
    public static bool ExecutionEnabled => false;
#endif

#if NET8_0_OR_GREATER
    public static bool DurationEnabled => ExecutionDuration.Enabled;
#else
    public static bool DurationEnabled => false;
#endif

#if NET9_0_OR_GREATER
    public static bool CircuitStateEnabled => CircuitStateGauge.Enabled;
    public static bool ConcurrencyStateEnabled =>
        ConcurrencyInflight.Enabled || ConcurrencyQueued.Enabled || ConcurrencyCapacity.Enabled;
    public static bool RateStateEnabled => RateAvailable.Enabled || RateQueued.Enabled;
#else
    public static bool CircuitStateEnabled => false;
    public static bool ConcurrencyStateEnabled => false;
    public static bool RateStateEnabled => false;
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

    public static long StartDuration() =>
#if NET8_0_OR_GREATER
        Stopwatch.GetTimestamp();
#else
        0;
#endif

    public static void Duration(long startedAt, string? shieldName, bool success)
    {
#if NET8_0_OR_GREATER
        if (startedAt != 0 && ExecutionDuration.Enabled)
        {
            var tags = NameTags(shieldName);
            tags.Add("kevlar.execution.outcome", success ? "success" : "failure");
            ExecutionDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalSeconds, tags);
        }
#endif
    }

    public static void RecordCircuitState(
        string? shieldName,
        int strategyIndex,
        CircuitState state)
    {
#if NET9_0_OR_GREATER
        if (CircuitStateGauge.Enabled)
        {
            CircuitStateGauge.Record(
                StateValue(state),
                StateTags(shieldName, strategyIndex));
        }
#endif
    }

    public static void RecordConcurrencyState(
        string? shieldName,
        int strategyIndex,
        long inflight,
        long queued,
        long capacity)
    {
#if NET9_0_OR_GREATER
        var tags = StateTags(shieldName, strategyIndex);
        if (ConcurrencyInflight.Enabled)
        {
            ConcurrencyInflight.Record(inflight, tags);
        }

        if (ConcurrencyQueued.Enabled)
        {
            ConcurrencyQueued.Record(queued, tags);
        }

        if (ConcurrencyCapacity.Enabled)
        {
            ConcurrencyCapacity.Record(capacity, tags);
        }
#endif
    }

    public static void RecordRateState(
        string? shieldName,
        int strategyIndex,
        long available,
        long queued)
    {
#if NET9_0_OR_GREATER
        var tags = StateTags(shieldName, strategyIndex);
        if (RateAvailable.Enabled)
        {
            RateAvailable.Record(available, tags);
        }

        if (RateQueued.Enabled)
        {
            RateQueued.Record(queued, tags);
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

#if NET9_0_OR_GREATER
    private static TagList StateTags(string? shieldName, int strategyIndex)
    {
        var tags = NameTags(shieldName);
        tags.Add("kevlar.strategy.index", BoxStrategyIndex(strategyIndex));
        return tags;
    }

    private static object BoxStrategyIndex(int strategyIndex) =>
        strategyIndex is >= MinimumCachedStrategyIndex and <= MaximumCachedStrategyIndex
            ? BoxedStrategyIndexes[strategyIndex - MinimumCachedStrategyIndex]
            : strategyIndex;

    private static object[] CreateBoxedStrategyIndexes()
    {
        var indexes = new object[MaximumCachedStrategyIndex - MinimumCachedStrategyIndex + 1];
        for (var index = MinimumCachedStrategyIndex; index <= MaximumCachedStrategyIndex; index++)
        {
            indexes[index - MinimumCachedStrategyIndex] = index;
        }

        return indexes;
    }
#endif

    private static string StateName(CircuitState state) => state switch
    {
        CircuitState.Closed => "closed",
        CircuitState.Open => "open",
        CircuitState.HalfOpen => "half_open",
        CircuitState.Isolated => "isolated",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static long StateValue(CircuitState state) => state switch
    {
        CircuitState.Closed => 0,
        CircuitState.Open => 1,
        CircuitState.HalfOpen => 2,
        CircuitState.Isolated => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
#endif
}

internal readonly record struct StrategyMetricAlias(string? ShieldName, int StrategyIndex);

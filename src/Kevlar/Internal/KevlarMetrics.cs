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

#if NET8_0_OR_GREATER
    private const int _minimumCachedStrategyIndex = -1;
    private const int _maximumCachedStrategyIndex = 63;
    private static readonly object[] _boxedStrategyIndexes = CreateBoxedStrategyIndexes();
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
    private static readonly Counter<long> PartitionEvictions = Meter.CreateCounter<long>(
        "kevlar.partitions.evictions",
        "{partition}",
        "Partitions removed from partitioned shield providers.");
    private static readonly Counter<long> CallbackErrors = Meter.CreateCounter<long>(
        "kevlar.callback_errors",
        "{error}",
        "Exceptions thrown by strategy notifications or observers.");
    private static readonly Histogram<double> ExecutionDuration = Meter.CreateHistogram<double>(
        "kevlar.execution.duration",
        "s",
        "Duration of completed public shield executions.");
    private static readonly Counter<long> StrategyEvents = Meter.CreateCounter<long>(
        "kevlar.strategy.events",
        "{event}",
        "Strategy and custom telemetry events.");
    private static readonly Histogram<double> AttemptDuration = Meter.CreateHistogram<double>(
        "kevlar.attempt.duration",
        "ms",
        "Duration of resilience execution attempts.");
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

#if NET8_0_OR_GREATER
    public static bool StrategyEventsEnabled => StrategyEvents.Enabled;
    public static bool AttemptDurationEnabled => AttemptDuration.Enabled;
#else
    public static bool StrategyEventsEnabled => false;
    public static bool AttemptDurationEnabled => false;
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

    public static void Timeout(KevlarContext context, string strategyName, Exception? exception)
    {
#if NET8_0_OR_GREATER
        if (Timeouts.Enabled)
        {
            Timeouts.Add(1, NameTags(context.ShieldName));
        }
#endif
        if (!KevlarTelemetry.EventEnabled)
        {
            return;
        }

        KevlarTelemetry.Record(
            context,
            strategyName,
            eventName: "timeout",
            KevlarTelemetrySeverity.Warning,
            context.StrategyIndex,
            attemptNumber: 0,
            isSuccess: false,
            exception);
    }

    public static void Hedge(
        KevlarContext context,
        string strategyName,
        int attemptNumber,
        Exception? exception = null)
    {
#if NET8_0_OR_GREATER
        if (Hedges.Enabled)
        {
            Hedges.Add(1, NameTags(context.ShieldName));
        }
#endif
        if (!KevlarTelemetry.EventEnabled)
        {
            return;
        }

        KevlarTelemetry.Record(
            context,
            strategyName,
            eventName: "hedge",
            exception is null
                ? KevlarTelemetrySeverity.Information
                : KevlarTelemetrySeverity.Warning,
            context.StrategyIndex,
            attemptNumber,
            isSuccess: exception is null,
            exception);
    }

    public static void Fallback(
        KevlarContext context,
        string strategyName,
        bool isSuccess,
        Exception? exception)
    {
#if NET8_0_OR_GREATER
        if (Fallbacks.Enabled)
        {
            Fallbacks.Add(1, NameTags(context.ShieldName));
        }
#endif
        if (!KevlarTelemetry.EventEnabled)
        {
            return;
        }

        KevlarTelemetry.Record(
            context,
            strategyName,
            eventName: "fallback",
            KevlarTelemetrySeverity.Warning,
            context.StrategyIndex,
            attemptNumber: 0,
            isSuccess,
            exception);
    }

    public static void Rejection(
        KevlarContext context,
        string kind,
        Exception exception,
        string? strategyName = null)
    {
#if NET8_0_OR_GREATER
        if (Rejections.Enabled)
        {
            var tags = NameTags(context.ShieldName);
            tags.Add("kevlar.rejection.type", kind);
            Rejections.Add(1, tags);
        }
#endif
        if (!KevlarTelemetry.EventEnabled)
        {
            return;
        }

        KevlarTelemetry.Record(
            context,
            strategyName ?? StrategyNameFromRejection(kind),
            eventName: "rejection",
            KevlarTelemetrySeverity.Warning,
            context.StrategyIndex,
            attemptNumber: 0,
            isSuccess: false,
            exception);
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

    public static void PartitionEviction(PartitionEvictionReason reason)
    {
#if NET8_0_OR_GREATER
        if (PartitionEvictions.Enabled)
        {
            PartitionEvictions.Add(1, new KeyValuePair<string, object?>(
                "kevlar.partition.reason",
                PartitionReasonName(reason)));
        }
#endif
    }

    public static void CallbackError(string? shieldName, CallbackErrorKind kind)
    {
#if NET8_0_OR_GREATER
        if (CallbackErrors.Enabled)
        {
            var tags = NameTags(shieldName);
            tags.Add("kevlar.callback.kind", CallbackKindName(kind));
            CallbackErrors.Add(1, tags);
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

    public static void StrategyEvent(
        in KevlarTelemetryEvent telemetryEvent,
        bool recordAttemptDuration)
    {
#if NET8_0_OR_GREATER
        if (!StrategyEvents.Enabled && (!recordAttemptDuration || !AttemptDuration.Enabled))
        {
            return;
        }

        var tags = NameTags(telemetryEvent.ShieldName);
        tags.Add("kevlar.strategy.index", BoxStrategyIndex(telemetryEvent.StrategyIndex));
        tags.Add("kevlar.strategy.name", telemetryEvent.StrategyName);
        tags.Add("kevlar.event.name", telemetryEvent.EventName);
        tags.Add("kevlar.event.severity", SeverityName(telemetryEvent.Severity));
        tags.Add("kevlar.attempt.number", BoxStrategyIndex(telemetryEvent.AttemptNumber));
        if (telemetryEvent.Exception is not null)
        {
            tags.Add("exception.type", telemetryEvent.Exception.GetType().FullName);
        }

        if (telemetryEvent.OperationKey is not null)
        {
            tags.Add("kevlar.operation.key", telemetryEvent.OperationKey);
        }

        if (StrategyEvents.Enabled)
        {
            StrategyEvents.Add(1, tags);
        }

        if (recordAttemptDuration && AttemptDuration.Enabled)
        {
            AttemptDuration.Record(telemetryEvent.Duration.TotalMilliseconds, tags);
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

    private static TagList StateTags(string? shieldName, int strategyIndex)
    {
        var tags = NameTags(shieldName);
        tags.Add("kevlar.strategy.index", BoxStrategyIndex(strategyIndex));
        return tags;
    }

    private static object BoxStrategyIndex(int strategyIndex) =>
        strategyIndex is >= _minimumCachedStrategyIndex and <= _maximumCachedStrategyIndex
            ? _boxedStrategyIndexes[strategyIndex - _minimumCachedStrategyIndex]
            : strategyIndex;

    private static object[] CreateBoxedStrategyIndexes()
    {
        var indexes = new object[_maximumCachedStrategyIndex - _minimumCachedStrategyIndex + 1];
        for (var index = _minimumCachedStrategyIndex; index <= _maximumCachedStrategyIndex; index++)
        {
            indexes[index - _minimumCachedStrategyIndex] = index;
        }

        return indexes;
    }

    private static string SeverityName(KevlarTelemetrySeverity severity) => severity switch
    {
        KevlarTelemetrySeverity.Information => "information",
        KevlarTelemetrySeverity.Warning => "warning",
        KevlarTelemetrySeverity.Error => "error",
        _ => "information",
    };
#endif

    private static string StateName(CircuitState state) => state switch
    {
        CircuitState.Closed => "closed",
        CircuitState.Open => "open",
        CircuitState.HalfOpen => "half_open",
        CircuitState.Isolated => "isolated",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string PartitionReasonName(PartitionEvictionReason reason) => reason switch
    {
        PartitionEvictionReason.Capacity => "capacity",
        PartitionEvictionReason.Idle => "idle",
        PartitionEvictionReason.Cleared => "cleared",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static string CallbackKindName(CallbackErrorKind kind) => kind switch
    {
        CallbackErrorKind.Retry => "retry",
        CallbackErrorKind.Timeout => "timeout",
        CallbackErrorKind.CircuitStateChanged => "circuit_state_changed",
        CallbackErrorKind.CircuitMonitor => "circuit_monitor",
        CallbackErrorKind.Hedge => "hedge",
        CallbackErrorKind.Fallback => "fallback",
        CallbackErrorKind.ConcurrencyLimitRejected => "concurrency_limit_rejected",
        CallbackErrorKind.RateLimitRejected => "rate_limit_rejected",
        CallbackErrorKind.RateLimiterAdapterRejected => "rate_limiter_adapter_rejected",
        CallbackErrorKind.ChaosInjected => "chaos_injected",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static long StateValue(CircuitState state) => state switch
    {
        CircuitState.Closed => 0,
        CircuitState.Open => 1,
        CircuitState.HalfOpen => 2,
        CircuitState.Isolated => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string StrategyNameFromRejection(string kind) => kind switch
    {
        "circuit_open" => "CircuitBreaker",
        "rate_limit" => "RateLimit",
        "rate_limiter_adapter" => "RateLimiterAdapter",
        "concurrency_limit" => "ConcurrencyLimit",
        _ => "Rejection",
    };
}

internal readonly record struct StrategyMetricAlias(string? ShieldName, int StrategyIndex);

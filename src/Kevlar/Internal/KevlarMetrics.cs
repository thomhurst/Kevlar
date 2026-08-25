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

#if NET9_0_OR_GREATER
    private static readonly StateMetricRegistry<CircuitBreakerStrategy> CircuitStates = new();
    private static readonly StateMetricRegistry<ConcurrencyLimitStrategy> ConcurrencyStates = new();
    private static readonly StateMetricRegistry<RateLimitStrategy> RateStates = new();
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
    private static readonly ObservableGauge<long> CircuitStateGauge = Meter.CreateObservableGauge(
        "kevlar.circuit_breaker.state",
        ObserveCircuitStates,
        "{state}",
        "Current circuit-breaker state: closed=0, open=1, half-open=2, isolated=3.");
    private static readonly ObservableGauge<long> ConcurrencyInflight = Meter.CreateObservableGauge(
        "kevlar.concurrency_limit.inflight",
        ObserveConcurrencyInflight,
        "{execution}",
        "Executions currently holding a concurrency permit.");
    private static readonly ObservableGauge<long> ConcurrencyQueued = Meter.CreateObservableGauge(
        "kevlar.concurrency_limit.queued",
        ObserveConcurrencyQueued,
        "{execution}",
        "Executions currently waiting for a concurrency permit.");
    private static readonly ObservableGauge<long> ConcurrencyCapacity = Meter.CreateObservableGauge(
        "kevlar.concurrency_limit.capacity",
        ObserveConcurrencyCapacity,
        "{execution}",
        "Configured concurrency permit capacity.");
    private static readonly ObservableGauge<long> RateAvailable = Meter.CreateObservableGauge(
        "kevlar.rate_limit.available",
        ObserveRateAvailable,
        "{permit}",
        "Immediately available rate-limit permits.");
    private static readonly ObservableGauge<long> RateQueued = Meter.CreateObservableGauge(
        "kevlar.rate_limit.queued",
        ObserveRateQueued,
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

    public static void Timeout(
        KevlarContext context,
        string strategyName,
        TimeSpan timeout,
        Exception? exception)
    {
#if NET8_0_OR_GREATER
        if (Timeouts.Enabled)
        {
            Timeouts.Add(1, NameTags(context.ShieldName));
        }
#endif
        if (!KevlarTelemetry.IsEventEnabled(context))
        {
            return;
        }

        KevlarTelemetry.Record(
            context,
            strategyName,
            eventName: "timeout",
            KevlarTelemetrySeverity.Warning,
            context.StrategyIndex,
            context.AttemptNumber,
            isSuccess: false,
            exception,
            duration: timeout);
    }

    public static void Hedge(
        KevlarContext context,
        string strategyName,
        int attemptNumber,
        Exception? exception = null,
        TimeSpan delay = default)
    {
#if NET8_0_OR_GREATER
        if (Hedges.Enabled)
        {
            Hedges.Add(1, NameTags(context.ShieldName));
        }
#endif
        if (!KevlarTelemetry.IsEventEnabled(context))
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
            exception,
            delay: delay);
    }

    public static void Fallback<T>(
        KevlarContext context,
        string strategyName,
        in Outcome<T> outcome)
    {
#if NET8_0_OR_GREATER
        if (Fallbacks.Enabled)
        {
            Fallbacks.Add(1, NameTags(context.ShieldName));
        }
#endif
        if (!KevlarTelemetry.IsEventEnabled(context))
        {
            return;
        }

        KevlarTelemetry.RecordResult(
            context,
            strategyName,
            eventName: "fallback",
            KevlarTelemetrySeverity.Warning,
            context.StrategyIndex,
            context.AttemptNumber,
            in outcome);
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
        if (!KevlarTelemetry.IsEventEnabled(context))
        {
            return;
        }

        KevlarTelemetry.Record(
            context,
            strategyName ?? StrategyNameFromRejection(kind),
            eventName: "rejection",
            KevlarTelemetrySeverity.Warning,
            context.StrategyIndex,
            context.AttemptNumber,
            isSuccess: false,
            exception,
            retryAfter: (exception as ExecutionRejectedException)?.RetryAfter,
            rejectionKind: kind);
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
        tags.Add(
            "kevlar.attempt.number",
            BoxStrategyIndex(Math.Clamp(
                telemetryEvent.AttemptNumber,
                0,
                _maximumCachedStrategyIndex)));
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

    public static StateMetricRegistration<CircuitBreakerStrategy> RegisterCircuitStateSource(
        CircuitBreakerStrategy strategy) =>
#if NET9_0_OR_GREATER
        CircuitStates.Register(strategy);
#else
        StateMetricRegistration<CircuitBreakerStrategy>.Disabled;
#endif

    public static StateMetricRegistration<ConcurrencyLimitStrategy> RegisterConcurrencyStateSource(
        ConcurrencyLimitStrategy strategy) =>
#if NET9_0_OR_GREATER
        ConcurrencyStates.Register(strategy);
#else
        StateMetricRegistration<ConcurrencyLimitStrategy>.Disabled;
#endif

    public static StateMetricRegistration<RateLimitStrategy> RegisterRateStateSource(
        RateLimitStrategy strategy) =>
#if NET9_0_OR_GREATER
        RateStates.Register(strategy);
#else
        StateMetricRegistration<RateLimitStrategy>.Disabled;
#endif

#if NET9_0_OR_GREATER
    private static IEnumerable<Measurement<long>> ObserveCircuitStates() =>
        CircuitStates.Observe(static (strategy, _) => StateValue(strategy.Core.State));

    private static IEnumerable<Measurement<long>> ObserveConcurrencyInflight() =>
        ConcurrencyStates.Observe(static (strategy, _) => strategy.CaptureState().Running);

    private static IEnumerable<Measurement<long>> ObserveConcurrencyQueued() =>
        ConcurrencyStates.Observe(static (strategy, _) => strategy.CaptureState().Queued);

    private static IEnumerable<Measurement<long>> ObserveConcurrencyCapacity() =>
        ConcurrencyStates.Observe(static (strategy, _) => strategy.MaxConcurrency);

    private static IEnumerable<Measurement<long>> ObserveRateAvailable() =>
        RateStates.Observe(static (strategy, timeProvider) => strategy.CaptureState(timeProvider!).Available);

    private static IEnumerable<Measurement<long>> ObserveRateQueued() =>
        RateStates.Observe(static (strategy, timeProvider) => strategy.CaptureState(timeProvider!).Queued);
#endif

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

#if NET9_0_OR_GREATER
    internal sealed class StateMetricRegistration<TStrategy>
        where TStrategy : class
    {
        private readonly Lock _gate = new();
        private readonly StateMetricRegistry<TStrategy> _registry;
        private readonly WeakReference<TStrategy> _strategy;
        private StateMetricObservation[] _observations = [];
        private int _published;

        public StateMetricRegistration(
            TStrategy strategy,
            StateMetricRegistry<TStrategy> registry)
        {
            _strategy = new WeakReference<TStrategy>(strategy);
            _registry = registry;
        }

        public StateMetricObservation[] Observations => Volatile.Read(ref _observations);

        public bool HasObservations => Observations.Length != 0;

        public bool TryGetStrategy(out TStrategy? strategy) => _strategy.TryGetTarget(out strategy);

        public void Add(StrategyMetricAlias alias, TimeProvider? timeProvider = null)
        {
            var observations = Volatile.Read(ref _observations);
            if (HasCurrentObservation(observations, alias, timeProvider))
            {
                return;
            }

            lock (_gate)
            {
                observations = _observations;
                if (TryUpdate(observations, alias, timeProvider))
                {
                    return;
                }

                if (observations.Length >= MaxTrackedStrategyAliases)
                {
                    var live = WithoutCollectedObservations(observations);
                    if (!ReferenceEquals(live, observations))
                    {
                        observations = live;
                        Volatile.Write(ref _observations, observations);
                    }

                    if (observations.Length >= MaxTrackedStrategyAliases)
                    {
                        return;
                    }
                }

                var updated = new StateMetricObservation[observations.Length + 1];
                observations.CopyTo(updated, 0);
                updated[^1] = new StateMetricObservation(alias, timeProvider);
                Volatile.Write(ref _observations, updated);
            }

            if (observations.Length == 0
                && Interlocked.Exchange(ref _published, 1) == 0)
            {
                _registry.Publish(this);
            }
        }

        public void RemoveCollectedObservations()
        {
            lock (_gate)
            {
                var observations = _observations;
                var live = WithoutCollectedObservations(observations);
                if (!ReferenceEquals(live, observations))
                {
                    Volatile.Write(ref _observations, live);
                }
            }
        }

        private static bool HasCurrentObservation(
            StateMetricObservation[] observations,
            StrategyMetricAlias alias,
            TimeProvider? timeProvider)
        {
            foreach (var observation in observations)
            {
                if (observation.Alias == alias)
                {
                    return observation.TryGetTimeProvider(out var currentTimeProvider)
                        && ReferenceEquals(currentTimeProvider, timeProvider);
                }
            }

            return false;
        }

        private static StateMetricObservation[] WithoutCollectedObservations(
            StateMetricObservation[] observations)
        {
            var firstCollected = -1;
            for (var index = 0; index < observations.Length; index++)
            {
                if (!observations[index].TryGetTimeProvider(out _))
                {
                    firstCollected = index;
                    break;
                }
            }

            if (firstCollected < 0)
            {
                return observations;
            }

            var live = new StateMetricObservation[observations.Length - 1];
            Array.Copy(observations, live, firstCollected);
            var count = firstCollected;
            for (var index = firstCollected + 1; index < observations.Length; index++)
            {
                var observation = observations[index];
                if (observation.TryGetTimeProvider(out _))
                {
                    live[count++] = observation;
                }
            }

            Array.Resize(ref live, count);
            return live;
        }

        private static bool TryUpdate(
            StateMetricObservation[] observations,
            StrategyMetricAlias alias,
            TimeProvider? timeProvider)
        {
            foreach (var observation in observations)
            {
                if (observation.Alias == alias)
                {
                    if (!observation.TryGetTimeProvider(out var currentTimeProvider)
                        || !ReferenceEquals(currentTimeProvider, timeProvider))
                    {
                        observation.SetTimeProvider(timeProvider);
                    }

                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class StateMetricRegistry<TStrategy>
        where TStrategy : class
    {
        private readonly Lock _gate = new();
        private WeakReference<StateMetricRegistration<TStrategy>>[] _registrations = [];

        public StateMetricRegistration<TStrategy> Register(TStrategy strategy) => new(strategy, this);

        public void Publish(StateMetricRegistration<TStrategy> registration)
        {
            lock (_gate)
            {
                var registrations = _registrations;
                var updated = new WeakReference<StateMetricRegistration<TStrategy>>[registrations.Length + 1];
                var count = 0;
                foreach (var reference in registrations)
                {
                    if (reference is not null && reference.TryGetTarget(out _))
                    {
                        updated[count++] = reference;
                    }
                }

                updated[count++] = new WeakReference<StateMetricRegistration<TStrategy>>(registration);
                if (count != updated.Length)
                {
                    Array.Resize(ref updated, count);
                }

                Volatile.Write(ref _registrations, updated);
            }
        }

        public IEnumerable<Measurement<long>> Observe(Func<TStrategy, TimeProvider?, long> observe)
        {
            var registrations = Volatile.Read(ref _registrations);
            var hasCollectedRegistration = false;
            foreach (var reference in registrations)
            {
                if (reference is null
                    || !reference.TryGetTarget(out var registration)
                    || !registration.TryGetStrategy(out var strategy))
                {
                    hasCollectedRegistration = true;
                    continue;
                }

                var hasCollectedObservation = false;
                foreach (var observation in registration.Observations)
                {
                    if (!observation.TryGetTimeProvider(out var timeProvider))
                    {
                        hasCollectedObservation = true;
                        continue;
                    }

                    yield return new Measurement<long>(
                        observe(strategy!, timeProvider),
                        StateTags(observation.Alias.ShieldName, observation.Alias.StrategyIndex));
                }

                if (hasCollectedObservation)
                {
                    registration.RemoveCollectedObservations();
                }
            }

            if (hasCollectedRegistration)
            {
                RemoveCollectedRegistrations();
            }
        }

        private void RemoveCollectedRegistrations()
        {
            lock (_gate)
            {
                var registrations = _registrations;
                var live = new WeakReference<StateMetricRegistration<TStrategy>>[registrations.Length];
                var count = 0;
                foreach (var reference in registrations)
                {
                    if (reference is not null
                        && reference.TryGetTarget(out var registration)
                        && registration.TryGetStrategy(out _))
                    {
                        live[count++] = reference;
                    }
                }

                if (count == registrations.Length)
                {
                    return;
                }

                Array.Resize(ref live, count);
                Volatile.Write(ref _registrations, live);
            }
        }
    }
#endif
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
        CallbackErrorKind.Logging => "logging",
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

#if !NET9_0_OR_GREATER
    internal sealed class StateMetricRegistration<TStrategy>
        where TStrategy : class
    {
        public static StateMetricRegistration<TStrategy> Disabled { get; } = new();

        public bool HasObservations => false;

        public void Add(StrategyMetricAlias alias, TimeProvider? timeProvider = null)
        {
        }
    }
#endif
}

internal readonly record struct StrategyMetricAlias(string? ShieldName, int StrategyIndex);

#if NET9_0_OR_GREATER
internal sealed class StateMetricObservation
{
    private WeakReference<TimeProvider>? _timeProvider;

    public StateMetricObservation(StrategyMetricAlias alias, TimeProvider? timeProvider)
    {
        Alias = alias;
        SetTimeProvider(timeProvider);
    }

    public StrategyMetricAlias Alias { get; }

    public bool TryGetTimeProvider(out TimeProvider? timeProvider)
    {
        var reference = Volatile.Read(ref _timeProvider);
        if (reference is null)
        {
            timeProvider = null;
            return true;
        }

        return reference.TryGetTarget(out timeProvider);
    }

    public void SetTimeProvider(TimeProvider? timeProvider) =>
        Volatile.Write(
            ref _timeProvider,
            timeProvider is null ? null : new WeakReference<TimeProvider>(timeProvider));
}
#endif

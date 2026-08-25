using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Kevlar.Internal;
using Kevlar.Strategies;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

/// <summary>
/// Guards the built-in metrics: every shield publishes counters through the "Kevlar" meter with
/// zero configuration. Assertions filter on a per-test <c>shield.name</c> tag so parallel tests
/// cannot cross-contaminate counts.
/// </summary>
[NotInParallel]
public class MetricsTests
{
    private sealed class KevlarMeterListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Action<string, long>? _onLongMeasurement;
        private readonly ConcurrentDictionary<string, Instrument> _instruments = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = [];
        private readonly ConcurrentQueue<(string Instrument, double Value, Dictionary<string, object?> Tags)> _doubleMeasurements = [];

        public KevlarMeterListener(Action<string, long>? onLongMeasurement = null)
        {
            _onLongMeasurement = onLongMeasurement;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName)
                {
                    _instruments[instrument.Name] = instrument;
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                _onLongMeasurement?.Invoke(instrument.Name, value);
                _measurements.Enqueue((instrument.Name, value, CaptureTags(tags)));
            });
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                _doubleMeasurements.Enqueue((instrument.Name, value, CaptureTags(tags)));
            });
            _listener.Start();
        }

        public long Total(string instrument, string? shieldName = null, params (string Key, string Value)[] tags) =>
            _measurements
                .Where(m => m.Instrument == instrument)
                .Where(m => shieldName is null || (m.Tags.TryGetValue("kevlar.shield.name", out var name) && Equals(name, shieldName)))
                .Where(m => tags.All(tag => m.Tags.TryGetValue(tag.Key, out var value) && Equals(value, tag.Value)))
                .Sum(m => m.Value);

        public IReadOnlyCollection<Instrument> Instruments => _instruments.Values.ToArray();

        public void RecordObservableInstruments() => _listener.RecordObservableInstruments();

        public IReadOnlyCollection<long> Values(string instrument, string? shieldName, bool requireName = true) =>
            _measurements
                .Where(measurement => measurement.Instrument == instrument)
                .Where(measurement => HasName(measurement.Tags, shieldName, requireName))
                .Select(measurement => measurement.Value)
                .ToArray();

        public IReadOnlyCollection<(long Value, Dictionary<string, object?> Tags)> LongMeasurements(
            string instrument,
            string? shieldName,
            bool requireName = true) =>
            _measurements
                .Where(measurement => measurement.Instrument == instrument)
                .Where(measurement => HasName(measurement.Tags, shieldName, requireName))
                .Select(measurement => (measurement.Value, measurement.Tags))
                .ToArray();

        public IReadOnlyCollection<(long Value, Dictionary<string, object?> Tags)> AllLongMeasurements(
            string instrument) =>
            _measurements
                .Where(measurement => measurement.Instrument == instrument)
                .Select(measurement => (measurement.Value, measurement.Tags))
                .ToArray();

        public IReadOnlyCollection<double> DoubleValues(string instrument, string? shieldName, bool requireName = true) =>
            _doubleMeasurements
                .Where(measurement => measurement.Instrument == instrument)
                .Where(measurement => HasName(measurement.Tags, shieldName, requireName))
                .Select(measurement => measurement.Value)
                .ToArray();

        public IReadOnlyCollection<Dictionary<string, object?>> DoubleMeasurements(
            string instrument,
            string? shieldName,
            bool requireName = true) =>
            _doubleMeasurements
                .Where(measurement => measurement.Instrument == instrument)
                .Where(measurement => HasName(measurement.Tags, shieldName, requireName))
                .Select(measurement => measurement.Tags)
                .ToArray();

        public IReadOnlyCollection<Dictionary<string, object?>> Measurements(
            string instrument,
            string? shieldName,
            bool requireName = true) =>
            _measurements
                .Where(measurement => measurement.Instrument == instrument)
                .Where(measurement => requireName
                    ? measurement.Tags.TryGetValue("kevlar.shield.name", out var name) && Equals(name, shieldName)
                    : !measurement.Tags.ContainsKey("kevlar.shield.name"))
                .Select(measurement => measurement.Tags)
                .ToArray();

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> CaptureTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var captured = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                captured[tag.Key] = tag.Value;
            }

            return captured;
        }

        private static bool HasName(Dictionary<string, object?> tags, string? shieldName, bool requireName) =>
            requireName
                ? tags.TryGetValue("kevlar.shield.name", out var name) && Equals(name, shieldName)
                : !tags.ContainsKey("kevlar.shield.name");
    }

    [Test]
    public async Task Executions_Are_Counted_With_Their_Outcome()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Retry(0, Backoff.None).WithName("metrics-executions");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(listener.Total("kevlar.executions", "metrics-executions", ("kevlar.execution.outcome", "success"))).IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.executions", "metrics-executions", ("kevlar.execution.outcome", "failure"))).IsEqualTo(1);
    }

    [Test]
    public async Task Empty_Shield_Executions_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Empty.WithName("metrics-empty");

        await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(listener.Total("kevlar.executions", "metrics-empty", ("kevlar.execution.outcome", "success")))
            .IsEqualTo(1);
    }

    [Test]
    public async Task Every_Public_Execution_Shape_Is_Counted_Once()
    {
        using var listener = new KevlarMeterListener();
        const string name = "metrics-shapes";
        var untyped = Shield.Empty.WithName(name);
        var typed = Shield<int>.Empty.WithName(name);
        Func<CancellationToken, Task<int>> taskResult = _ => Task.FromResult(42);
        Func<CancellationToken, Task> taskVoid = _ => Task.CompletedTask;

        await untyped.ExecuteAsync(_ => new ValueTask<int>(42));
        await untyped.ExecuteAsync(taskResult);
        await untyped.ExecuteAsync(_ => ValueTask.CompletedTask);
        await untyped.ExecuteAsync(taskVoid);
        _ = await untyped.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));
        _ = await untyped.ExecuteOutcomeAsync(taskResult);
        _ = untyped.Execute(_ => 42);
        untyped.Execute(_ => { });

        await typed.ExecuteAsync(_ => new ValueTask<int>(42));
        await typed.ExecuteAsync(taskResult);
        _ = await typed.ExecuteOutcomeAsync(_ => new ValueTask<int>(42));
        _ = await typed.ExecuteOutcomeAsync(taskResult);
        _ = typed.Execute(_ => 42);

        await Assert.That(listener.Total("kevlar.executions", name, ("kevlar.execution.outcome", "success")))
            .IsEqualTo(13);
    }

    [Test]
    public async Task PreCancelled_Executions_Are_Failures_And_Skip_The_Delegate()
    {
        using var listener = new KevlarMeterListener();
        using var cancellation = new CancellationTokenSource();
        const string name = "metrics-pre-cancelled";
        var shield = Shield.Empty.WithName(name);
        var invoked = false;
        cancellation.Cancel();

        await Assert.That(async () => await shield.ExecuteAsync(_ =>
        {
            invoked = true;
            return new ValueTask<int>(42);
        }, cancellation.Token)).Throws<OperationCanceledException>();
        var outcome = await shield.ExecuteOutcomeAsync(_ =>
        {
            invoked = true;
            return new ValueTask<int>(42);
        }, cancellation.Token);
        await Assert.That(() => shield.Execute(_ =>
        {
            invoked = true;
            return 42;
        }, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(invoked).IsFalse();
        await Assert.That(outcome.IsSuccess).IsFalse();
        await Assert.That(listener.Total("kevlar.executions", name, ("kevlar.execution.outcome", "failure")))
            .IsEqualTo(3);
    }

    [Test]
    public async Task Initialization_Failure_Is_Recorded_Before_OnCompleted()
    {
        using var listener = new KevlarMeterListener();
        const string name = "metrics-initialization-failure";
        var failuresObserved = 0L;

        await Assert.That(async () => await Shield.Empty.WithName(name).ExecuteWithContextAsync(
                0,
                static (_, _) => throw new InvalidOperationException("initialize"),
                static (_, _) => new ValueTask<int>(42),
                (_, _) => failuresObserved = listener.Total(
                    "kevlar.executions",
                    name,
                    ("kevlar.execution.outcome", "failure"))))
            .Throws<InvalidOperationException>();

        await Assert.That(failuresObserved).IsEqualTo(1);
    }

    [Test]
    public async Task Meter_And_Instrument_Schema_Is_Stable()
    {
        using var listener = new KevlarMeterListener();
        await Shield.Empty.WithName("metrics-schema")
            .ExecuteAsync(_ => new ValueTask<int>(42));

        var expectedInstruments = new Dictionary<string, string>
        {
            ["kevlar.executions"] = "{execution}",
            ["kevlar.retries"] = "{retry}",
            ["kevlar.timeouts"] = "{timeout}",
            ["kevlar.hedges"] = "{hedge}",
            ["kevlar.fallbacks"] = "{fallback}",
            ["kevlar.rejections"] = "{rejection}",
            ["kevlar.circuit_breaker.transitions"] = "{transition}",
            ["kevlar.partitions.evictions"] = "{partition}",
            ["kevlar.callback_errors"] = "{error}",
            ["kevlar.execution.duration"] = "s",
            ["kevlar.strategy.events"] = "{event}",
            ["kevlar.attempt.duration"] = "ms",
#if NET9_0_OR_GREATER
            ["kevlar.circuit_breaker.state"] = "{state}",
            ["kevlar.concurrency_limit.inflight"] = "{execution}",
            ["kevlar.concurrency_limit.queued"] = "{execution}",
            ["kevlar.concurrency_limit.capacity"] = "{execution}",
            ["kevlar.rate_limit.available"] = "{permit}",
            ["kevlar.rate_limit.queued"] = "{execution}",
#endif
        };

        await Assert.That(listener.Instruments.Select(instrument => instrument.Name))
            .IsEquivalentTo(expectedInstruments.Keys);
        await Assert.That(listener.Instruments
            .Where(instrument => instrument.Name is "kevlar.execution.duration" or "kevlar.attempt.duration")
            .All(instrument => instrument is Histogram<double>)).IsTrue();
        await Assert.That(listener.Instruments
            .Where(instrument => instrument.Name is not ("kevlar.execution.duration" or "kevlar.attempt.duration"))
            .All(instrument => instrument is Counter<long> or Gauge<long>)).IsTrue();
        await Assert.That(listener.Instruments.All(instrument => instrument.Meter.Name == "Kevlar")).IsTrue();
        await Assert.That(listener.Instruments.All(instrument => instrument.Meter.Version == "1.0")).IsTrue();
        await Assert.That(listener.Instruments.All(instrument =>
            expectedInstruments.TryGetValue(instrument.Name, out var unit) && instrument.Unit == unit))
            .IsTrue();
        await Assert.That(listener.Instruments.All(instrument => !string.IsNullOrWhiteSpace(instrument.Description)))
            .IsTrue();
    }

    [Test]
    public async Task Partition_Evictions_Include_Reason_But_Not_Key()
    {
        using var listener = new KevlarMeterListener();
        var provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions { MaximumPartitions = 1 });
        _ = provider.GetShield("sensitive-tenant-key");

        _ = provider.GetShield("replacement");

        var tags = listener.Measurements(
            "kevlar.partitions.evictions",
            shieldName: null,
            requireName: false).Single();
        await Assert.That(tags.Keys).IsEquivalentTo(["kevlar.partition.reason"]);
        await Assert.That(tags["kevlar.partition.reason"]).IsEqualTo("capacity");
        await Assert.That(tags.Values).DoesNotContain("sensitive-tenant-key");
    }

    [Test]
    public async Task Partition_Eviction_Metric_Reentrancy_Uses_The_Active_Reservation()
    {
        PartitionedShield<string>? provider = null;
        Shield? nested = null;
        var handled = false;
        var factoryCalls = 0;
        using var listener = new KevlarMeterListener((instrument, _) =>
        {
            if (handled || instrument != "kevlar.partitions.evictions")
            {
                return;
            }

            handled = true;
            nested = provider!.GetShield("nested");
        });
        provider = new PartitionedShield<string>(
            key => Shield.Empty.WithName($"{key}-{Interlocked.Increment(ref factoryCalls)}"),
            new PartitionedShieldOptions { MaximumPartitions = 1 });
        _ = provider.GetShield("first");

        var replacement = await Task.Run(() => provider.GetShield("replacement"))
            .WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(nested).IsNotNull();
        await Assert.That(provider.TryGetShield("nested", out _)).IsFalse();
        await Assert.That(provider.TryGetShield("replacement", out var retained)).IsTrue();
        await Assert.That(retained).IsSameReferenceAs(replacement);
        await Assert.That(factoryCalls).IsEqualTo(3);
        await Assert.That(provider.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Name_And_Outcome_Tag_Schema_Is_Stable()
    {
        using var listener = new KevlarMeterListener();
        var unnamed = Shield.Empty;
        var emptyName = Shield.Empty.WithName(string.Empty);
        var named = Shield.Empty.WithName("metrics-tags");

        await unnamed.ExecuteAsync(_ => new ValueTask<int>(1));
        await emptyName.ExecuteAsync(_ => new ValueTask<int>(2));
        await named.ExecuteAsync(_ => new ValueTask<int>(3));

        var unnamedTags = listener.Measurements("kevlar.executions", null, requireName: false);
        var emptyTags = listener.Measurements("kevlar.executions", string.Empty).Single();
        var namedTags = listener.Measurements("kevlar.executions", "metrics-tags").Single();
        await Assert.That(unnamedTags.Any(tags => tags.Keys.SequenceEqual(["kevlar.execution.outcome"]))).IsTrue();
        await Assert.That(emptyTags.Keys).IsEquivalentTo(["kevlar.shield.name", "kevlar.execution.outcome"]);
        await Assert.That(emptyTags["kevlar.shield.name"]).IsEqualTo(string.Empty);
        await Assert.That(namedTags.Keys).IsEquivalentTo(["kevlar.shield.name", "kevlar.execution.outcome"]);
        await Assert.That(namedTags["kevlar.execution.outcome"]).IsEqualTo("success");
    }

    [Test]
    public async Task Retries_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Retry(2, Backoff.None).WithName("metrics-retries");

        var attempts = 0;
        await shield.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException();
            }

            return new ValueTask<int>(1);
        });

        await Assert.That(listener.Total("kevlar.retries", "metrics-retries")).IsEqualTo(2);
    }

    [Test]
    public async Task Retry_Attempts_Record_Duration_And_Documented_Tags()
    {
        using var listener = new KevlarMeterListener();
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var shield = Shield.Retry(2, Backoff.None)
            .WithTimeProvider(timeProvider)
            .WithName("metrics-attempts");

        var result = await shield.ExecuteWithContextAsync(
            "checkout",
            static (operation, properties) => properties.Set(KevlarKeys.OperationKey, operation),
            (_, _) =>
            {
                timeProvider.Advance(TimeSpan.FromMilliseconds(10));
                return ++attempts < 3
                    ? ValueTask.FromException<int>(new InvalidOperationException("unique-message"))
                    : new ValueTask<int>(42);
            });

        var durations = listener.DoubleValues("kevlar.attempt.duration", "metrics-attempts");
        var attemptTags = listener.DoubleMeasurements("kevlar.attempt.duration", "metrics-attempts");
        var retryTags = listener.Measurements("kevlar.strategy.events", "metrics-attempts")
            .Where(tags => Equals(tags["kevlar.event.name"], "retry"))
            .ToArray();

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(durations).IsEquivalentTo([10d, 10d, 10d]);
        await Assert.That(attemptTags.Select(tags => (int)tags["kevlar.attempt.number"]!))
            .IsEquivalentTo([0, 1, 2]);
        await Assert.That(attemptTags.All(tags =>
            Equals(tags["kevlar.strategy.name"], "Retry")
            && Equals(tags["kevlar.operation.key"], "checkout")
            && !tags.Values.Contains("unique-message"))).IsTrue();
        await Assert.That(retryTags.Length).IsEqualTo(2);
        await Assert.That(retryTags.All(tags =>
            Equals(tags["exception.type"], typeof(InvalidOperationException).FullName))).IsTrue();
    }

    [Test]
    public async Task Attempt_Number_Metric_Tag_Is_Capped_But_Listener_Value_Is_Exact()
    {
        using var meterListener = new KevlarMeterListener();
        var listenerAttempts = new List<int>();
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "execution_attempt")
            {
                listenerAttempts.Add(telemetryEvent.AttemptNumber);
            }
        }));
        var shield = Shield.Retry(65, Backoff.None)
            .WithName("metrics-bounded-attempt-number");

        _ = await shield.ExecuteOutcomeAsync<int>(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("retry")));

        var metricAttempts = meterListener
            .DoubleMeasurements("kevlar.attempt.duration", "metrics-bounded-attempt-number")
            .Select(tags => (int)tags["kevlar.attempt.number"]!)
            .ToArray();
        await Assert.That(listenerAttempts).IsEquivalentTo(Enumerable.Range(0, 66));
        await Assert.That(metricAttempts.Max()).IsEqualTo(63);
        await Assert.That(metricAttempts.Distinct().Count()).IsEqualTo(64);
    }

    [Test]
    public async Task Missing_Optional_Values_Produce_The_Exact_Bounded_Tag_Set()
    {
        using var listener = new KevlarMeterListener();

        _ = await Shield.Retry(0, Backoff.None)
            .WithName("metrics-bounded-tags")
            .ExecuteAsync(_ => new ValueTask<int>(42));

        var tags = listener.DoubleMeasurements("kevlar.attempt.duration", "metrics-bounded-tags").Single();
        await Assert.That(tags.Keys).IsEquivalentTo([
            "kevlar.shield.name",
            "kevlar.strategy.index",
            "kevlar.strategy.name",
            "kevlar.event.name",
            "kevlar.event.severity",
            "kevlar.attempt.number",
        ]);
        await Assert.That(tags["kevlar.event.severity"]).IsEqualTo("information");
    }

    [Test]
    public async Task Listener_Receives_Ordered_Events_And_Cannot_Change_Outcome()
    {
        var observed = new List<TelemetrySnapshot>();
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            observed.Add(new TelemetrySnapshot(
                telemetryEvent.EventName,
                telemetryEvent.AttemptNumber,
                telemetryEvent.OperationKey,
                telemetryEvent.Context.Properties.GetOrDefault(KevlarKeys.OperationKey, string.Empty)));
            if (telemetryEvent.EventName == "retry")
            {
                throw new InvalidOperationException("listener");
            }
        }));
        var attempts = 0;

        var result = await Shield.Retry(1, Backoff.None).ExecuteWithContextAsync(
            "listener-operation",
            static (operation, properties) => properties.Set(KevlarKeys.OperationKey, operation),
            (_, _) => ++attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException("action"))
                : new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observed.Select(item => item.EventName).SequenceEqual(
            ["execution_attempt", "retry", "execution_attempt"])).IsTrue();
        await Assert.That(observed.Select(item => item.AttemptNumber).SequenceEqual([0, 1, 1])).IsTrue();
        await Assert.That(observed.All(item =>
            item.OperationKey == "listener-operation"
            && item.ContextOperationKey == "listener-operation")).IsTrue();
    }

    [Test]
    public async Task Disposing_An_Equal_Listener_Removes_The_Exact_Subscription()
    {
        var firstEvents = 0;
        var secondEvents = 0;
        using var firstSubscription = KevlarDiagnostics.Listen(
            new EqualTelemetryListener(() => firstEvents++));
        var secondSubscription = KevlarDiagnostics.Listen(
            new EqualTelemetryListener(() => secondEvents++));

        secondSubscription.Dispose();
        _ = await Shield.Retry(0, Backoff.None)
            .ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(firstEvents).IsGreaterThan(0);
        await Assert.That(secondEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Custom_Strategy_Can_Record_Into_Listener_And_Meter()
    {
        using var listener = new KevlarMeterListener();
        TelemetrySnapshot observed = default;
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
            observed = new TelemetrySnapshot(
                telemetryEvent.EventName,
                telemetryEvent.AttemptNumber,
                telemetryEvent.OperationKey,
                telemetryEvent.Context.Properties.GetOrDefault(KevlarKeys.OperationKey, string.Empty))));
        var shield = Shield.Use(new TelemetryStrategy()).WithName("metrics-custom-event");

        _ = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        var tags = listener.Measurements("kevlar.strategy.events", "metrics-custom-event").Single();
        await Assert.That(observed.EventName).IsEqualTo("custom_strategy_event");
        await Assert.That(tags["kevlar.event.name"]).IsEqualTo("custom_strategy_event");
        await Assert.That(tags["kevlar.strategy.name"]).IsEqualTo("Custom");
    }

    [Test]
    public async Task Strategy_Name_Tag_Uses_Options_Name_Or_Built_In_Default()
    {
        using var listener = new KevlarMeterListener();
        var namedAttempts = 0;
        var named = Shield.Retry(options =>
        {
            options.Name = "catalog-retry";
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
        }).WithName("metrics-named-strategy");

        _ = await named.ExecuteAsync(_ => ++namedAttempts == 1
            ? ValueTask.FromException<int>(new InvalidOperationException())
            : new ValueTask<int>(42));
        _ = await Shield.Retry(0, Backoff.None)
            .WithName("metrics-default-strategy")
            .ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(listener.Measurements("kevlar.strategy.events", "metrics-named-strategy")
            .All(tags => Equals(tags["kevlar.strategy.name"], "catalog-retry"))).IsTrue();
        await Assert.That(listener.Measurements("kevlar.strategy.events", "metrics-default-strategy")
            .All(tags => Equals(tags["kevlar.strategy.name"], "Retry"))).IsTrue();
    }

    [Test]
    public async Task Circuit_State_Changes_Emit_Ordered_Strategy_Events()
    {
        using var listener = new KevlarMeterListener();
        var timeProvider = new FakeTimeProvider();
        var circuit = Shield.CircuitBreaker(options =>
        {
            options.Name = "inventory-circuit";
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
        }).WithName("metrics-circuit-events").WithTimeProvider(timeProvider);

        _ = await circuit.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        _ = await circuit.ExecuteAsync(_ => new ValueTask<int>(42));

        var events = listener.Measurements("kevlar.strategy.events", "metrics-circuit-events")
            .Where(tags => Equals(tags["kevlar.strategy.name"], "inventory-circuit"))
            .Select(tags => (string)tags["kevlar.event.name"]!)
            .ToArray();
        await Assert.That(events.SequenceEqual(
            ["circuit_opened", "circuit_half_opened", "circuit_closed"])).IsTrue();
    }

    [Test]
    public async Task Timeouts_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Timeout(TimeSpan.FromMilliseconds(50)).WithName("metrics-timeouts");

        await Assert.That(async () => await shield.ExecuteAsync(async ct => await Task.Delay(Timeout.InfiniteTimeSpan, ct)))
            .Throws<TimeoutExceededException>();

        await Assert.That(listener.Total("kevlar.timeouts", "metrics-timeouts")).IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.executions", "metrics-timeouts", ("kevlar.execution.outcome", "failure")))
            .IsEqualTo(1);
    }

    [Test]
    public async Task Timeout_Telemetry_Uses_The_Surfaced_Exception()
    {
        using var meterListener = new KevlarMeterListener();
        KevlarTelemetryEvent observed = default;
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "timeout")
            {
                observed = telemetryEvent;
            }
        }));
        var shield = Shield.Timeout(TimeSpan.FromMilliseconds(20))
            .WithName("metrics-timeout-exception");

        var exception = await Assert.That(async () => await shield.ExecuteAsync(
                async token => await Task.Delay(Timeout.InfiniteTimeSpan, token)))
            .Throws<TimeoutExceededException>();

        await Assert.That(ReferenceEquals(observed.Exception, exception)).IsTrue();
        var tags = meterListener.Measurements("kevlar.strategy.events", "metrics-timeout-exception")
            .Single();
        await Assert.That(tags["exception.type"])
            .IsEqualTo(typeof(TimeoutExceededException).FullName);
    }

    [Test]
    public async Task Nested_Timeout_Telemetry_Uses_The_Retry_Attempt()
    {
        KevlarTelemetryEvent observed = default;
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "timeout")
            {
                observed = telemetryEvent;
            }
        }));
        var attempts = 0;
        var shield = Shield.Retry(1, Backoff.None)
            .Timeout(TimeSpan.FromMilliseconds(20));

        _ = await Assert.That(async () => await shield.ExecuteAsync(async token =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("retry");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        })).Throws<TimeoutExceededException>();

        await Assert.That(observed.AttemptNumber).IsEqualTo(1);
    }

    [Test]
    public async Task Nested_Fallback_Telemetry_Uses_The_Retry_Attempt()
    {
        var attempts = new List<int>();
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "fallback")
            {
                attempts.Add(telemetryEvent.AttemptNumber);
            }
        }));
        var fallbackCalls = 0;
        var shield = Shield.For<int>()
            .When<ArgumentException>()
            .Retry(1, Backoff.None)
            .When<InvalidOperationException>()
            .Fallback(_ => ++fallbackCalls == 1
                ? ValueTask.FromException<int>(new ArgumentException("fallback"))
                : new ValueTask<int>(42));

        var result = await shield.ExecuteAsync(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("action")));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEquivalentTo([0, 1]);
    }

    [Test]
    public async Task Nested_Rejection_Telemetry_Uses_The_Retry_Attempt()
    {
        KevlarTelemetryEvent observed = default;
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "rejection")
            {
                observed = telemetryEvent;
            }
        }));
        var shield = Shield.Retry(1, Backoff.None)
            .RateLimit(1, TimeSpan.FromHours(1));

        _ = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("action"))))
            .Throws<RateLimitExceededException>();

        await Assert.That(observed.AttemptNumber).IsEqualTo(1);
    }

    [Test]
    public async Task Nested_Circuit_Telemetry_Uses_The_Retry_Attempt()
    {
        KevlarTelemetryEvent observed = default;
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "circuit_opened")
            {
                observed = telemetryEvent;
            }
        }));
        var shield = Shield.Retry(1, Backoff.None)
            .CircuitBreaker(2, TimeSpan.FromMinutes(1));

        _ = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("action"))))
            .Throws<InvalidOperationException>();

        await Assert.That(observed.AttemptNumber).IsEqualTo(1);
    }

    [Test]
    public async Task Custom_Event_Defaults_To_The_Active_Retry_Attempt()
    {
        var attempts = new List<int>();
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "custom_strategy_event")
            {
                attempts.Add(telemetryEvent.AttemptNumber);
            }
        }));
        var calls = 0;
        var shield = Shield.Retry(1, Backoff.None)
            .Use(new TelemetryStrategy());

        var result = await shield.ExecuteAsync(_ => ++calls == 1
            ? ValueTask.FromException<int>(new InvalidOperationException("action"))
            : new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEquivalentTo([0, 1]);
    }

    [Test]
    public async Task Custom_Event_Uses_The_Active_Hedge_Attempt()
    {
        var attempts = new ConcurrentQueue<int>();
        var startedAttempts = 0;
        var bothAttemptsStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName != "custom_strategy_event")
            {
                return;
            }

            attempts.Enqueue(telemetryEvent.AttemptNumber);
            if (Interlocked.Increment(ref startedAttempts) == 2)
            {
                bothAttemptsStarted.TrySetResult();
            }
        }));
        var shield = Shield.Hedge(1, TimeSpan.Zero)
            .Use(new TelemetryStrategy());

        var result = await shield.ExecuteAsync(async _ =>
        {
            await bothAttemptsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return 42;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEquivalentTo([0, 1]);
    }

    [Test]
    public async Task Primary_Hedge_Attempt_Resets_The_Outer_Retry_Attempt()
    {
        var attempts = new List<int>();
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "custom_strategy_event")
            {
                attempts.Add(telemetryEvent.AttemptNumber);
            }
        }));
        var calls = 0;
        var shield = Shield.Retry(1, Backoff.None)
            .Hedge(1, Timeout.InfiniteTimeSpan)
            .Use(new TelemetryStrategy());

        var result = await shield.ExecuteAsync(_ => ++calls <= 2
            ? ValueTask.FromException<int>(new InvalidOperationException("retry"))
            : new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(calls).IsEqualTo(3);
        await Assert.That(attempts).IsEquivalentTo([0, 1, 0]);
    }

    [Test]
    public async Task Wrapped_Cancellations_Are_Counted_As_Timeouts()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Timeout(TimeSpan.FromMilliseconds(20)).WithName("metrics-wrapped-timeouts");

        var exception = await Assert.That(async () => await shield.ExecuteAsync(async cancellationToken =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw new TaskCanceledException("transport wrapper");
            }
        })).Throws<TimeoutExceededException>();

        await Assert.That(exception!.InnerException).IsTypeOf<TaskCanceledException>();
        await Assert.That(listener.Total("kevlar.timeouts", "metrics-wrapped-timeouts")).IsEqualTo(1);
    }

    [Test]
    public async Task Rate_Limit_Rejections_Are_Counted_With_Their_Kind()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.RateLimit(1, TimeSpan.FromHours(1)).WithName("metrics-rate");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();

        await Assert.That(listener.Total("kevlar.rejections", "metrics-rate", ("kevlar.rejection.type", "rate_limit"))).IsEqualTo(1);
    }

    [Test]
    public async Task Every_Rejection_Kind_Is_Counted_Exactly_Once()
    {
        using var listener = new KevlarMeterListener();
        var rejectionEvents = new ConcurrentQueue<KevlarTelemetryEvent>();
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "rejection")
            {
                rejectionEvents.Enqueue(telemetryEvent);
            }
        }));
        var rateLimit = Shield.RateLimit(1, TimeSpan.FromHours(1)).WithName("metrics-reject-rate");
        await rateLimit.ExecuteAsync(_ => new ValueTask<int>(1));
        var rateRejection = await Assert.That(async () => await rateLimit.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();

        var circuit = Shield.CircuitBreaker(1, TimeSpan.FromHours(1)).WithName("metrics-reject-circuit");
        _ = await circuit.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        var circuitRejection = await Assert.That(async () => await circuit.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrency = Shield.ConcurrencyLimit(1).WithName("metrics-reject-concurrency");
        var occupying = concurrency.ExecuteAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await entered.Task;
        ConcurrencyLimitExceededException? concurrencyRejection = null;
        try
        {
            concurrencyRejection = await Assert.That(async () => await concurrency.ExecuteAsync(_ => new ValueTask<int>(2)))
                .Throws<ConcurrencyLimitExceededException>();
        }
        finally
        {
            release.TrySetResult();
        }

        _ = await occupying;

        await Assert.That(listener.Total("kevlar.rejections", "metrics-reject-rate", ("kevlar.rejection.type", "rate_limit")))
            .IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.rejections", "metrics-reject-circuit", ("kevlar.rejection.type", "circuit_open")))
            .IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.rejections", "metrics-reject-concurrency", ("kevlar.rejection.type", "concurrency_limit")))
            .IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.executions", "metrics-reject-rate", ("kevlar.execution.outcome", "success")))
            .IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.executions", "metrics-reject-rate", ("kevlar.execution.outcome", "failure")))
            .IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.executions", "metrics-reject-circuit", ("kevlar.execution.outcome", "failure")))
            .IsEqualTo(2);
        await Assert.That(listener.Total("kevlar.executions", "metrics-reject-concurrency", ("kevlar.execution.outcome", "success")))
            .IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.executions", "metrics-reject-concurrency", ("kevlar.execution.outcome", "failure")))
            .IsEqualTo(1);

        var expectedExceptions = new Dictionary<string, Exception?>
        {
            ["metrics-reject-rate"] = rateRejection,
            ["metrics-reject-circuit"] = circuitRejection,
            ["metrics-reject-concurrency"] = concurrencyRejection,
        };
        foreach (var (shieldName, exception) in expectedExceptions)
        {
            var telemetryEvent = rejectionEvents.Single(item => item.ShieldName == shieldName);
            await Assert.That(ReferenceEquals(telemetryEvent.Exception, exception)).IsTrue();
            var tags = listener.Measurements("kevlar.strategy.events", shieldName).Single(item =>
                item.TryGetValue("kevlar.event.name", out var eventName)
                && Equals(eventName, "rejection"));
            await Assert.That(tags["exception.type"]).IsEqualTo(exception!.GetType().FullName);
        }
    }

    [Test]
    public async Task Nested_Strategies_Record_One_Execution_And_Exact_Events()
    {
        using var listener = new KevlarMeterListener();
        const string name = "metrics-nested";
        var attempts = 0;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .FallbackTo(42)
            .Retry(2, Backoff.None)
            .WithName(name);

        var result = await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(listener.Total("kevlar.executions", name, ("kevlar.execution.outcome", "success"))).IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.retries", name)).IsEqualTo(2);
        await Assert.That(listener.Total("kevlar.fallbacks", name)).IsEqualTo(1);
    }

    [Test]
    public async Task Fallback_Metric_Is_Recorded_Before_Sync_And_Async_Notifications()
    {
        using var listener = new KevlarMeterListener();
        const string name = "metrics-fallback-notifications";
        var syncObservedMetric = false;
        var asyncObservedMetric = false;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .FallbackTo(
                42,
                options =>
                {
                    options.OnFallback = _ => syncObservedMetric = listener.Total("kevlar.fallbacks", name) == 1;
                    options.OnFallbackAsync = _ =>
                    {
                        asyncObservedMetric = listener.Total("kevlar.fallbacks", name) == 1;
                        return ValueTask.CompletedTask;
                    };
                })
            .WithName(name);

        var result = await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(syncObservedMetric).IsTrue();
        await Assert.That(asyncObservedMetric).IsTrue();
    }

    [Test]
    public async Task Rejected_Result_Execution_Does_Not_Record_A_Void_Fallback()
    {
        using var listener = new KevlarMeterListener();
        const string name = "metrics-invalid-void-fallback";
        var shield = Shield.Empty
            .Fallback(static _ => ValueTask.CompletedTask)
            .WithName(name);

        await Assert.That(async () => await shield.ExecuteOutcomeAsync<int>(
                static _ => ValueTask.FromException<int>(new InvalidOperationException())))
            .Throws<InvalidOperationException>();

        await Assert.That(listener.Total("kevlar.fallbacks", name)).IsEqualTo(0);
    }

    [Test]
    public async Task Concurrent_Execution_Totals_Are_Exact_And_Isolated_By_Name()
    {
        using var listener = new KevlarMeterListener();
        const int countPerShield = 250;
        const string firstName = "metrics-concurrent-first";
        const string secondName = "metrics-concurrent-second";
        var first = Shield.Empty.WithName(firstName);
        var second = Shield.Empty.WithName(secondName);

        var executions = Enumerable.Range(0, countPerShield * 2)
            .Select(async index =>
            {
                var shield = index % 2 == 0 ? first : second;
                return await shield.ExecuteAsync(async _ =>
                {
                    await Task.Yield();
                    return index;
                });
            });
        _ = await Task.WhenAll(executions);

        await Assert.That(listener.Total("kevlar.executions", firstName, ("kevlar.execution.outcome", "success")))
            .IsEqualTo(countPerShield);
        await Assert.That(listener.Total("kevlar.executions", secondName, ("kevlar.execution.outcome", "success")))
            .IsEqualTo(countPerShield);
    }

    [Test]
    public async Task Every_Circuit_Transition_Direction_Is_Emitted()
    {
        using var listener = new KevlarMeterListener();
        var before = CircuitTransitionTotals(listener);

        await EmitNaturalCircuitTransitions();
        await EmitIsolationTransitions(CircuitState.Closed);
        await EmitIsolationTransitions(CircuitState.Open);
        await EmitIsolationTransitions(CircuitState.HalfOpen);
        await EmitOpenResetTransition();

        var after = CircuitTransitionTotals(listener);
        var expectedDirections = new[]
        {
            (CircuitState.Closed, CircuitState.Open),
            (CircuitState.Open, CircuitState.HalfOpen),
            (CircuitState.HalfOpen, CircuitState.Closed),
            (CircuitState.HalfOpen, CircuitState.Open),
            (CircuitState.Closed, CircuitState.Isolated),
            (CircuitState.Open, CircuitState.Isolated),
            (CircuitState.HalfOpen, CircuitState.Isolated),
            (CircuitState.Open, CircuitState.Closed),
            (CircuitState.Isolated, CircuitState.Closed),
        };

        foreach (var direction in expectedDirections)
        {
            await Assert.That(after[direction] > before[direction]).IsTrue();
        }
    }

    [Test]
    public async Task Fallbacks_And_Circuit_Transitions_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .FallbackTo(-1)
            .CircuitBreaker(1, TimeSpan.FromMinutes(1))
            .WithName("metrics-fallback");

        var recovered = await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(recovered).IsEqualTo(-1);
        await Assert.That(listener.Total("kevlar.fallbacks", "metrics-fallback")).IsEqualTo(1);

        // The breaker tripped Closed -> Open; transitions carry no shield name, and other tests
        // may trip breakers concurrently, so assert at least ours was recorded.
        var transitions = listener.Total(
            "kevlar.circuit_breaker.transitions",
            null,
            ("kevlar.circuit_breaker.state.from", "closed"),
            ("kevlar.circuit_breaker.state.to", "open"));
        await Assert.That(transitions >= 1).IsTrue();
    }

    [Test]
    public async Task Hedged_Attempts_Are_Counted()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.Hedge(1, TimeSpan.Zero).WithName("metrics-hedge");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(listener.Total("kevlar.hedges", "metrics-hedge")).IsEqualTo(1);
    }

    [Test]
    public async Task Failed_Hedge_Generation_Records_The_Exception()
    {
        using var meterListener = new KevlarMeterListener();
        var generatorFailure = new InvalidOperationException("generator failure");
        KevlarTelemetryEvent observed = default;
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "hedge")
            {
                observed = telemetryEvent;
            }
        }));
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = _ => throw generatorFailure;
        }).WithName("metrics-failed-hedge");

        var outcome = await shield.ExecuteOutcomeAsync(_ => throw new InvalidOperationException("primary failure"));

        await Assert.That(ReferenceEquals(outcome.Exception, generatorFailure)).IsTrue();
        await Assert.That(observed.EventName).IsEqualTo("hedge");
        await Assert.That(observed.IsSuccess).IsFalse();
        await Assert.That(ReferenceEquals(observed.Exception, generatorFailure)).IsTrue();
        var tags = meterListener.Measurements("kevlar.strategy.events", "metrics-failed-hedge").Single();
        await Assert.That(tags["exception.type"]).IsEqualTo(typeof(InvalidOperationException).FullName);
    }

    [Test]
    public async Task Suppressed_Hedged_Attempts_Are_Not_Counted()
    {
        using var listener = new KevlarMeterListener();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ => cancellation.Cancel();
        }).WithName("metrics-suppressed-hedge");

        await shield.ExecuteOutcomeAsync<int>(async token =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token);

        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(listener.Total("kevlar.hedges", "metrics-suppressed-hedge")).IsEqualTo(0);
    }

    [Test]
    public async Task Execution_Duration_Covers_Named_Unnamed_And_Failed_Calls()
    {
        using var listener = new KevlarMeterListener();
        using var cancellation = new CancellationTokenSource();
        var named = Shield.Empty.WithName("metrics-duration");

        await named.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(async () => await named.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        cancellation.Cancel();
        await Assert.That(async () => await named.ExecuteAsync(
                _ => new ValueTask<int>(3),
                cancellation.Token))
            .Throws<OperationCanceledException>();
        await Shield.Empty.ExecuteAsync(_ => new ValueTask<int>(2));

        var namedValues = listener.DoubleValues("kevlar.execution.duration", "metrics-duration");
        var namedTags = listener.DoubleMeasurements("kevlar.execution.duration", "metrics-duration");
        await Assert.That(namedValues.Count).IsEqualTo(3);
        await Assert.That(namedValues.All(value => value >= 0)).IsTrue();
        await Assert.That(namedTags.Select(tags => (string)tags["kevlar.execution.outcome"]!))
            .IsEquivalentTo(["success", "failure", "failure"]);
        await Assert.That(listener.DoubleValues("kevlar.execution.duration", null, requireName: false).Count >= 1)
            .IsTrue();
    }

    [Test]
    public async Task Execution_Duration_Is_Recorded_Before_Completion_Observers()
    {
        using var listener = new KevlarMeterListener();
        const string shieldName = "metrics-completion-order";
        var observedCounts = new List<int>();
        var shield = Shield.Empty.WithName(shieldName);

        await shield.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            static (_, _) => new ValueTask<int>(42),
            (_, _) => observedCounts.Add(listener.DoubleValues(
                "kevlar.execution.duration",
                shieldName).Count));

        await shield.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            static async (_, _) =>
            {
                await Task.Yield();
                return 42;
            },
            (_, _) => observedCounts.Add(listener.DoubleValues(
                "kevlar.execution.duration",
                shieldName).Count));

        await Assert.That(observedCounts.Count).IsEqualTo(2);
        await Assert.That(observedCounts[0]).IsEqualTo(1);
        await Assert.That(observedCounts[1]).IsEqualTo(2);
    }

    [Test]
    public async Task Execution_Duration_Excludes_Execution_Counter_Listener_Time()
    {
        using var listener = new KevlarMeterListener((instrument, _) =>
        {
            if (instrument == "kevlar.executions")
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }
        });
        var shield = Shield.Empty.WithName("metrics-duration-listener-overhead");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);

        await Assert.That(listener.DoubleValues(
                "kevlar.execution.duration",
                "metrics-duration-listener-overhead").Single())
            .IsLessThan(0.05);
    }

#if NET9_0_OR_GREATER
    [Test]
    public async Task Circuit_State_Gauge_Reports_Every_State()
    {
        using var listener = new KevlarMeterListener();
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        }).WithTimeProvider(timeProvider).WithName("metrics-circuit-state");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        listener.RecordObservableInstruments();
        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        listener.RecordObservableInstruments();
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await shield.ExecuteAsync(_ =>
        {
            listener.RecordObservableInstruments();
            return new ValueTask<int>(2);
        });
        monitor.Isolate();
        listener.RecordObservableInstruments();
        _ = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(3));

        await Assert.That(listener.Values("kevlar.circuit_breaker.state", "metrics-circuit-state"))
            .Contains(0)
            .And.Contains(1)
            .And.Contains(2)
            .And.Contains(3);
    }

    [Test]
    public async Task Manual_Circuit_Transitions_Update_State_Gauge()
    {
        using var listener = new KevlarMeterListener();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options => options.Monitor = monitor)
            .WithName("metrics-manual-circuit-state");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        listener.RecordObservableInstruments();
        var closedMeasurements = listener.Values(
            "kevlar.circuit_breaker.state",
            "metrics-manual-circuit-state").Count(value => value == 0);

        monitor.Isolate();
        listener.RecordObservableInstruments();
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-manual-circuit-state"))
            .Contains(3);

        monitor.Reset();
        listener.RecordObservableInstruments();
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-manual-circuit-state").Count(value => value == 0))
            .IsEqualTo(closedMeasurements + 1);
    }

    [Test]
    public async Task Provisional_Unnamed_Circuit_Series_Stays_Current()
    {
        using var listener = new KevlarMeterListener();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options => options.Monitor = monitor)
            .WithName("metrics-provisional-circuit-name");

        monitor.Isolate();
        _ = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        monitor.Reset();
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                shieldName: null,
                requireName: false).Last())
            .IsEqualTo(0);
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-provisional-circuit-name").Last())
            .IsEqualTo(0);
    }

    [Test]
    public async Task Disabled_Circuit_Gauge_Does_Not_Create_An_Unnamed_Alias()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options => options.Monitor = monitor)
            .WithName("metrics-disabled-circuit-alias");
        monitor.Isolate();

        using var listener = new KevlarMeterListener();
        listener.RecordObservableInstruments();
        var unnamedMeasurements = listener.Values(
            "kevlar.circuit_breaker.state",
            shieldName: null,
            requireName: false).Count;
        _ = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        monitor.Reset();
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                shieldName: null,
                requireName: false).Count)
            .IsEqualTo(unnamedMeasurements * 2);
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-disabled-circuit-alias").Last())
            .IsEqualTo(0);
    }

    [Test]
    public async Task Shared_Circuit_Updates_Every_Named_Alias()
    {
        using var listener = new KevlarMeterListener();
        var monitor = new CircuitBreakerMonitor();
        var shared = Shield.CircuitBreaker(options => options.Monitor = monitor);
        var first = shared.WithName("metrics-circuit-alias-first");
        var second = shared.WithName("metrics-circuit-alias-second");

        await first.ExecuteAsync(_ => ValueTask.CompletedTask);
        await second.ExecuteAsync(_ => ValueTask.CompletedTask);
        monitor.Isolate();
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-circuit-alias-first").Last())
            .IsEqualTo(3);
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-circuit-alias-second").Last())
            .IsEqualTo(3);

        monitor.Reset();
        listener.RecordObservableInstruments();
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-circuit-alias-first").Last())
            .IsEqualTo(0);
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-circuit-alias-second").Last())
            .IsEqualTo(0);
    }

    [Test]
    public async Task State_Gauge_Callbacks_Run_Only_During_Collection()
    {
        var callbacks = 0;
        using var listener = new KevlarMeterListener((instrument, _) =>
        {
            if (instrument == "kevlar.concurrency_limit.inflight")
            {
                callbacks++;
            }
        });
        var shield = Shield.ConcurrencyLimit(1).WithName("metrics-collection-only");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);

        await Assert.That(callbacks).IsEqualTo(0);
        listener.RecordObservableInstruments();
        await Assert.That(callbacks).IsGreaterThan(0);
    }

    [Test]
    public async Task State_Gauge_Listener_Failure_Does_Not_Fail_Execution()
    {
        var callbackInvoked = false;
        using var listener = new KevlarMeterListener((instrument, _) =>
        {
            if (instrument == "kevlar.rate_limit.available")
            {
                callbackInvoked = true;
                throw new InvalidOperationException("metrics callback");
            }
        });
        var shield = Shield.RateLimit(10, TimeSpan.FromSeconds(1))
            .WithName("metrics-listener-failure");

        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(42))).IsEqualTo(42);

        try
        {
            listener.RecordObservableInstruments();
        }
        catch (AggregateException)
        {
            // Listener exceptions belong to collection, never shield execution.
        }

        await Assert.That(callbackInvoked).IsTrue();
    }

    [Test]
    public async Task State_Gauges_Disambiguate_Strategies_By_Pipeline_Index()
    {
        using var listener = new KevlarMeterListener();
        const string name = "metrics-independent-strategies";
        var shield = Shield.ConcurrencyLimit(1)
            .Wrap(Shield.ConcurrencyLimit(10))
            .WithName(name);

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        listener.RecordObservableInstruments();

        var measurements = listener.LongMeasurements(
            "kevlar.concurrency_limit.capacity",
            name);
        await Assert.That(measurements.Select(measurement =>
                (int)measurement.Tags["kevlar.strategy.index"]!).Distinct())
            .IsEquivalentTo([0, 1]);
        await Assert.That(measurements.Select(measurement => measurement.Value).Distinct())
            .IsEquivalentTo([1L, 10L]);
    }

    [Test]
    public async Task Circuit_Alias_Tracking_Is_Bounded()
    {
        using var listener = new KevlarMeterListener();
        var monitor = new CircuitBreakerMonitor();
        var shared = Shield.CircuitBreaker(options => options.Monitor = monitor);

        for (var index = 0; index <= KevlarMetrics.MaxTrackedStrategyAliases; index++)
        {
            await shared.WithName($"metrics-bounded-alias-{index}")
                .ExecuteAsync(_ => ValueTask.CompletedTask);
        }

        monitor.Isolate();
        listener.RecordObservableInstruments();

        var isolatedAliases = listener.AllLongMeasurements("kevlar.circuit_breaker.state")
            .Where(measurement => measurement.Value == 3)
            .Select(measurement => measurement.Tags.TryGetValue("kevlar.shield.name", out var name)
                ? name as string
                : null)
            .Where(name => name is not null
                && name.StartsWith("metrics-bounded-alias-", StringComparison.Ordinal))
            .Distinct()
            .Count();
        await Assert.That(isolatedAliases).IsEqualTo(KevlarMetrics.MaxTrackedStrategyAliases);
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                $"metrics-bounded-alias-{KevlarMetrics.MaxTrackedStrategyAliases}").Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task Full_Live_Alias_Registration_Does_Not_Allocate_For_Overflow()
    {
        var registry = new KevlarMetrics.StateMetricRegistry<object>();
        var strategy = new object();
        var registration = registry.Register(strategy);
        for (var index = 0; index < KevlarMetrics.MaxTrackedStrategyAliases; index++)
        {
            registration.Add(new StrategyMetricAlias($"metrics-full-alias-{index}", 0));
        }

        var overflow = new StrategyMetricAlias("metrics-overflow-alias", 0);
        registration.Add(overflow);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            registration.Add(overflow);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0);
        GC.KeepAlive(strategy);
    }

    [Test]
    public async Task Immediately_Admitted_Execution_Is_Not_Reported_As_Queued()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.ConcurrencyLimit(1).WithName("metrics-immediate-concurrency");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        listener.RecordObservableInstruments();

        var queued = listener.Values(
            "kevlar.concurrency_limit.queued",
            "metrics-immediate-concurrency");
        await Assert.That(queued.Count).IsGreaterThan(0);
        await Assert.That(queued.All(value => value == 0)).IsTrue();
    }

    [Test]
    public async Task Concurrent_Completions_Leave_An_Idle_Concurrency_Snapshot()
    {
        using var listener = new KevlarMeterListener();
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var shield = Shield.ConcurrencyLimit(2).WithName("metrics-concurrent-completion");
        async ValueTask Enter(CancellationToken _)
        {
            if (Interlocked.Increment(ref entered) == 2)
            {
                bothEntered.TrySetResult();
            }

            await release.Task;
        }

        var first = shield.ExecuteAsync(Enter).AsTask();
        var second = shield.ExecuteAsync(Enter).AsTask();
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.inflight",
                "metrics-concurrent-completion").Last())
            .IsEqualTo(0);
        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.queued",
                "metrics-concurrent-completion").Last())
            .IsEqualTo(0);
    }

    [Test]
    public async Task Shared_Concurrency_Updates_Every_Named_Alias()
    {
        using var listener = new KevlarMeterListener();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = Shield.ConcurrencyLimit(1, 1);
        var first = shared.WithName("metrics-concurrency-alias-first");
        var second = shared.WithName("metrics-concurrency-alias-second");

        var firstExecution = first.ExecuteAsync(async _ =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task;
        }).AsTask();
        await firstEntered.Task;
        var secondExecution = second.ExecuteAsync(async _ =>
        {
            secondEntered.TrySetResult();
            await releaseSecond.Task;
        }).AsTask();

        releaseFirst.TrySetResult();
        await secondEntered.Task;
        await firstExecution;
        releaseSecond.TrySetResult();
        await secondExecution;
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.inflight",
                "metrics-concurrency-alias-first").Last())
            .IsEqualTo(0);
        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.queued",
                "metrics-concurrency-alias-first").Last())
            .IsEqualTo(0);
        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.inflight",
                "metrics-concurrency-alias-second").Last())
            .IsEqualTo(0);
        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.queued",
                "metrics-concurrency-alias-second").Last())
            .IsEqualTo(0);
    }

    [Test]
    public async Task Concurrency_Inflight_Never_Exceeds_Capacity_During_Handoffs()
    {
        using var listener = new KevlarMeterListener();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.ConcurrencyLimit(1, 32).WithName("metrics-concurrency-handoffs");
        var holder = shield.ExecuteAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }).AsTask();
        await entered.Task;

        var queued = Enumerable.Range(0, 32)
            .Select(_ => shield.ExecuteAsync(_ => ValueTask.CompletedTask).AsTask())
            .ToArray();
        release.TrySetResult();
        await Task.WhenAll(queued.Prepend(holder));
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.inflight",
                "metrics-concurrency-handoffs").All(value => value <= 1))
            .IsTrue();
    }

    [Test]
    public async Task Concurrency_Queued_Never_Includes_Rejected_Callers()
    {
        using var listener = new KevlarMeterListener();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.ConcurrencyLimit(1).WithName("metrics-concurrency-rejections");
        var holder = shield.ExecuteAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }).AsTask();
        await entered.Task;

        var rejections = Enumerable.Range(0, 8)
            .Select(worker => Task.Run(() =>
            {
                for (var attempt = 0; attempt < 5_000; attempt++)
                {
                    _ = shield.ExecuteOutcome(static _ => { });
                }
            }))
            .ToArray();
        while (rejections.Any(static task => !task.IsCompleted))
        {
            listener.RecordObservableInstruments();
            await Task.Yield();
        }

        await Task.WhenAll(rejections);
        listener.RecordObservableInstruments();
        release.TrySetResult();
        await holder;

        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.queued",
                "metrics-concurrency-rejections").All(static value => value == 0))
            .IsTrue();
    }

    [Test]
    public async Task Concurrency_Reservation_Is_Not_Reported_As_Queued()
    {
        var strategy = new ConcurrencyLimitStrategy(new ConcurrencyLimitOptions
        {
            MaxConcurrency = 1,
            QueueLimit = 0,
        });
        var reserve = typeof(ConcurrencyLimitStrategy).GetMethod(
            "TryReserveCapacity",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        await Assert.That((bool)reserve.Invoke(strategy, null)!).IsTrue();
        await Assert.That(strategy.CaptureState().Queued).IsEqualTo(0);
    }

    [Test]
    public async Task Concurrency_Parked_Permit_Is_Reported_As_Available()
    {
        var strategy = new ConcurrencyLimitStrategy(new ConcurrencyLimitOptions
        {
            MaxConcurrency = 1,
            QueueLimit = 1,
        });
        var acquire = typeof(ConcurrencyLimitStrategy).GetMethod(
            "TryAcquirePermit",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var release = typeof(ConcurrencyLimitStrategy).GetMethod(
            "ReleasePermit",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var waiters = typeof(ConcurrencyLimitStrategy).GetField(
            "_waiters",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        await Assert.That((bool)acquire.Invoke(strategy, null)!).IsTrue();
        waiters.SetValue(strategy, 1);
        release.Invoke(strategy, null);
        waiters.SetValue(strategy, 0);

        var state = strategy.CaptureState();
        await Assert.That(state.Available).IsEqualTo(1);
        await Assert.That(state.Running).IsEqualTo(0);
    }

    [Test]
    public async Task Concurrency_Queued_Permit_Transition_Updates_State_Atomically()
    {
        var strategy = new ConcurrencyLimitStrategy(new ConcurrencyLimitOptions
        {
            MaxConcurrency = 2,
            QueueLimit = 1,
        });
        var state = typeof(ConcurrencyLimitStrategy).GetField(
            "_state",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var acquireQueued = typeof(ConcurrencyLimitStrategy).GetMethod(
            "TryAcquireQueuedPermit",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        state.SetValue(strategy, 1L);
        await Assert.That((bool)acquireQueued.Invoke(strategy, [false])!).IsTrue();

        var snapshot = strategy.CaptureState();
        await Assert.That(snapshot.Available).IsEqualTo(1);
        await Assert.That(snapshot.Running).IsEqualTo(1);
        await Assert.That(snapshot.Queued).IsEqualTo(0);
    }

    [Test]
    public async Task State_Registry_Compaction_Tolerates_Collected_Entries()
    {
        var registry = new KevlarMetrics.StateMetricRegistry<object>();
        var registration = registry.Register(new object());
        var registrations = typeof(KevlarMetrics.StateMetricRegistry<object>).GetField(
            "_registrations",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        registrations.SetValue(
            registry,
            new WeakReference<KevlarMetrics.StateMetricRegistration<object>>[] { null! });

        registry.Publish(registration);
        await Assert.That(((Array)registrations.GetValue(registry)!)
                .Cast<object?>()
                .All(static item => item is not null))
            .IsTrue();

        registrations.SetValue(
            registry,
            new WeakReference<KevlarMetrics.StateMetricRegistration<object>>[] { null! });
        _ = registry.Observe(static (_, _) => 0).ToArray();

        await Assert.That(((Array)registrations.GetValue(registry)!).Length).IsEqualTo(0);
    }

    [Test]
    public async Task State_Registration_Is_Published_Only_Once()
    {
        var registry = new KevlarMetrics.StateMetricRegistry<object>();
        var strategy = new object();
        var registration = registry.Register(strategy);
        var firstProvider = AddCollectibleStateObservation(registration);

        for (var attempt = 0; firstProvider.IsAlive && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        _ = registry.Observe(static (_, _) => 0).ToArray();
        var secondProvider = new FakeTimeProvider();
        registration.Add(new StrategyMetricAlias("metrics-republished-state", 0), secondProvider);
        var observations = registry.Observe(static (_, _) => 0).ToArray();

        await Assert.That(firstProvider.IsAlive).IsFalse();
        await Assert.That(observations.Length).IsEqualTo(1);
        GC.KeepAlive(strategy);
        GC.KeepAlive(secondProvider);
    }

    [Test]
    public async Task State_Registration_Reclaims_Dead_Aliases_Before_Enforcing_Cap()
    {
        var registry = new KevlarMetrics.StateMetricRegistry<object>();
        var strategy = new object();
        var registration = registry.Register(strategy);
        var providers = Enumerable.Range(0, KevlarMetrics.MaxTrackedStrategyAliases)
            .Select(index => AddCollectibleStateObservation(
                registration,
                $"metrics-collected-alias-{index}"))
            .ToArray();

        for (var attempt = 0; providers.Any(static provider => provider.IsAlive) && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        var liveProvider = new FakeTimeProvider();
        var liveAlias = new StrategyMetricAlias("metrics-live-after-collected-aliases", 0);
        registration.Add(liveAlias, liveProvider);

        await Assert.That(providers.All(static provider => !provider.IsAlive)).IsTrue();
        await Assert.That(registration.Observations.Select(static observation => observation.Alias))
            .IsEquivalentTo([liveAlias]);
        GC.KeepAlive(strategy);
        GC.KeepAlive(liveProvider);
    }

    [Test]
    public async Task State_Registration_Serializes_Provider_Revival_With_Compaction()
    {
        var registry = new KevlarMetrics.StateMetricRegistry<object>();
        var strategy = new object();
        var registration = registry.Register(strategy);
        var alias = new StrategyMetricAlias("metrics-provider-revival", 0);
        var oldProvider = AddCollectibleStateObservation(registration, alias.ShieldName!);

        for (var attempt = 0; oldProvider.IsAlive && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        var gateField = typeof(KevlarMetrics.StateMetricRegistration<object>).GetField(
            "_gate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var gate = (Lock)gateField.GetValue(registration)!;
        var liveProvider = new FakeTimeProvider();
        using var started = new ManualResetEventSlim();
        Task revival;
        bool startedWhileCompactionHeld;
        bool completedWhileCompactionHeld;
        using (gate.EnterScope())
        {
            revival = Task.Run(() =>
            {
                started.Set();
                registration.Add(alias, liveProvider);
            });
            startedWhileCompactionHeld = started.Wait(TimeSpan.FromSeconds(5));
            completedWhileCompactionHeld = startedWhileCompactionHeld
                && revival.Wait(TimeSpan.FromMilliseconds(200));
        }

        await revival.WaitAsync(TimeSpan.FromSeconds(5));
        registration.RemoveCollectedObservations();

        await Assert.That(oldProvider.IsAlive).IsFalse();
        await Assert.That(startedWhileCompactionHeld).IsTrue();
        await Assert.That(completedWhileCompactionHeld).IsFalse();
        await Assert.That(registration.Observations).Count().IsEqualTo(1);
        await Assert.That(registration.Observations[0].TryGetTimeProvider(out var retainedProvider))
            .IsTrue();
        await Assert.That(ReferenceEquals(retainedProvider, liveProvider)).IsTrue();
        GC.KeepAlive(strategy);
        GC.KeepAlive(liveProvider);
    }

    [Test]
    public async Task Concurrency_Gauges_Track_Inflight_Queue_And_Cancellation()
    {
        using var listener = new KevlarMeterListener();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.ConcurrencyLimit(1, 1).WithName("metrics-concurrency-state");
        var occupying = shield.ExecuteAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await entered.Task;
        listener.RecordObservableInstruments();
        var queued = shield.ExecuteAsync(_ => new ValueTask<int>(2), cancellation.Token).AsTask();
        listener.RecordObservableInstruments();
        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        listener.RecordObservableInstruments();
        release.SetResult();
        _ = await occupying;
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values("kevlar.concurrency_limit.inflight", "metrics-concurrency-state"))
            .Contains(1)
            .And.Contains(0);
        await Assert.That(listener.Values("kevlar.concurrency_limit.queued", "metrics-concurrency-state"))
            .Contains(1)
            .And.Contains(0);
        await Assert.That(listener.Values("kevlar.concurrency_limit.capacity", "metrics-concurrency-state")
            .All(value => value == 1)).IsTrue();

        await Shield.ConcurrencyLimit(1).ExecuteAsync(_ => new ValueTask<int>(3));
        listener.RecordObservableInstruments();
        await Assert.That(listener.Values("kevlar.concurrency_limit.inflight", null, requireName: false).Count > 0)
            .IsTrue();
    }

    [Test]
    public async Task Rate_Gauges_Track_Available_Permits_Queue_And_Cancellation()
    {
        using var listener = new KevlarMeterListener();
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 2;
            options.Burst = 2;
            options.Window = TimeSpan.FromSeconds(1);
            options.QueueLimit = 1;
        }).WithTimeProvider(timeProvider).WithName("metrics-rate-state");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        listener.RecordObservableInstruments();
        await shield.ExecuteAsync(_ => new ValueTask<int>(2));
        listener.RecordObservableInstruments();
        var queued = shield.ExecuteAsync(_ => new ValueTask<int>(3), cancellation.Token).AsTask();
        listener.RecordObservableInstruments();
        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values("kevlar.rate_limit.available", "metrics-rate-state"))
            .Contains(1)
            .And.Contains(0);
        await Assert.That(listener.Values("kevlar.rate_limit.queued", "metrics-rate-state"))
            .Contains(1)
            .And.Contains(0);
    }

    [Test]
    public async Task Concurrent_Cancellations_Leave_An_Empty_Rate_Queue_Snapshot()
    {
        using var listener = new KevlarMeterListener();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromHours(1);
            options.QueueLimit = 2;
        }).WithTimeProvider(timeProvider).WithName("metrics-concurrent-rate-cancellation");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        var first = shield.ExecuteAsync(
            _ => ValueTask.CompletedTask,
            firstCancellation.Token).AsTask();
        var second = shield.ExecuteAsync(
            _ => ValueTask.CompletedTask,
            secondCancellation.Token).AsTask();
        listener.RecordObservableInstruments();
        await Assert.That(listener.Values(
                "kevlar.rate_limit.queued",
                "metrics-concurrent-rate-cancellation"))
            .Contains(2);

        await Task.WhenAll(
            Task.Run(firstCancellation.Cancel),
            Task.Run(secondCancellation.Cancel));
        await Assert.That(async () => await first).Throws<OperationCanceledException>();
        await Assert.That(async () => await second).Throws<OperationCanceledException>();
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values(
                "kevlar.rate_limit.queued",
                "metrics-concurrent-rate-cancellation").Last())
            .IsEqualTo(0);
    }

    [Test]
    public async Task Shared_Rate_Limit_Updates_Every_Named_Alias()
    {
        using var listener = new KevlarMeterListener();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var shared = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromHours(1);
            options.QueueLimit = 2;
        }).WithTimeProvider(timeProvider);
        var first = shared.WithName("metrics-rate-alias-first");
        var second = shared.WithName("metrics-rate-alias-second");

        await first.ExecuteAsync(_ => ValueTask.CompletedTask);
        var firstQueued = first.ExecuteAsync(
            _ => ValueTask.CompletedTask,
            firstCancellation.Token).AsTask();
        var secondQueued = second.ExecuteAsync(
            _ => ValueTask.CompletedTask,
            secondCancellation.Token).AsTask();

        firstCancellation.Cancel();
        await Assert.That(async () => await firstQueued).Throws<OperationCanceledException>();
        secondCancellation.Cancel();
        await Assert.That(async () => await secondQueued).Throws<OperationCanceledException>();
        listener.RecordObservableInstruments();

        await Assert.That(listener.Values(
                "kevlar.rate_limit.queued",
                "metrics-rate-alias-first").Last())
            .IsEqualTo(0);
        await Assert.That(listener.Values(
                "kevlar.rate_limit.queued",
                "metrics-rate-alias-second").Last())
            .IsEqualTo(0);
    }

    [Test]
    public async Task State_Gauges_Do_Not_Retain_Collected_Strategies()
    {
        using var listener = new KevlarMeterListener();
        var strategy = CreateCollectibleStateStrategy();

        for (var attempt = 0; strategy.IsAlive && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        listener.RecordObservableInstruments();

        await Assert.That(strategy.IsAlive).IsFalse();
    }

    [Test]
    public async Task State_Gauges_Do_Not_Retain_Time_Providers_Without_Collection()
    {
        using var listener = new KevlarMeterListener();
        var timeProvider = CreateCollectibleStateTimeProvider();

        for (var attempt = 0; timeProvider.IsAlive && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        await Assert.That(timeProvider.IsAlive).IsFalse();
    }

    [Test]
    public async Task State_Gauges_Do_Not_Retain_Providers_For_Abandoned_Shield_Aliases()
    {
        using var listener = new KevlarMeterListener();
        var shared = Shield.RateLimit(1, TimeSpan.FromMinutes(1));
        var timeProvider = CreateCollectibleStateTimeProviderAlias(shared);

        for (var attempt = 0; timeProvider.IsAlive && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        listener.RecordObservableInstruments();

        await Assert.That(timeProvider.IsAlive).IsFalse();
        await Assert.That(listener.AllLongMeasurements("kevlar.rate_limit.available")
                .Any(measurement => measurement.Tags.TryGetValue(
                    "kevlar.shield.name",
                    out var name)
                    && Equals(name, "metrics-collectible-provider-alias")))
            .IsFalse();
        GC.KeepAlive(shared);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateCollectibleStateStrategy()
    {
        var shield = Shield.ConcurrencyLimit(1).WithName("metrics-collectible-strategy");
        shield.Execute(static _ => { });
        return new WeakReference(shield.Strategies[0]);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateCollectibleStateTimeProvider()
    {
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.RateLimit(1, TimeSpan.FromMinutes(1))
            .WithName("metrics-collectible-time-provider")
            .WithTimeProvider(timeProvider);
        shield.Execute(static _ => { });
        return new WeakReference(timeProvider);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateCollectibleStateTimeProviderAlias(Shield shared)
    {
        var timeProvider = new FakeTimeProvider();
        shared
            .WithName("metrics-collectible-provider-alias")
            .WithTimeProvider(timeProvider)
            .Execute(static _ => { });
        return new WeakReference(timeProvider);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AddCollectibleStateObservation(
        KevlarMetrics.StateMetricRegistration<object> registration)
        => AddCollectibleStateObservation(registration, "metrics-republished-state");

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AddCollectibleStateObservation(
        KevlarMetrics.StateMetricRegistration<object> registration,
        string alias)
    {
        var timeProvider = new FakeTimeProvider();
        registration.Add(new StrategyMetricAlias(alias, 0), timeProvider);
        return new WeakReference(timeProvider);
    }

#endif

    private static Dictionary<(CircuitState From, CircuitState To), long> CircuitTransitionTotals(
        KevlarMeterListener listener) =>
        (from source in Enum.GetValues<CircuitState>()
         from target in Enum.GetValues<CircuitState>()
         select (source, target)).ToDictionary(
            direction => direction,
            direction => listener.Total(
                "kevlar.circuit_breaker.transitions",
                null,
                ("kevlar.circuit_breaker.state.from", StateName(direction.source)),
                ("kevlar.circuit_breaker.state.to", StateName(direction.target))));

    private static string StateName(CircuitState state) => state switch
    {
        CircuitState.Closed => "closed",
        CircuitState.Open => "open",
        CircuitState.HalfOpen => "half_open",
        CircuitState.Isolated => "isolated",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static async Task EmitNaturalCircuitTransitions()
    {
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(1)).WithTimeProvider(timeProvider);

        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        _ = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
    }

    private static async Task EmitIsolationTransitions(CircuitState state)
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        }).WithTimeProvider(timeProvider);

        if (state is CircuitState.Open or CircuitState.HalfOpen)
        {
            _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        }

        if (state == CircuitState.HalfOpen)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            var probe = shield.ExecuteAsync(async _ =>
            {
                entered.SetResult();
                await release.Task;
                return 1;
            }).AsTask();
            await entered.Task;
            try
            {
                monitor.Isolate();
            }
            finally
            {
                release.TrySetResult();
            }

            _ = await probe;
        }
        else
        {
            monitor.Isolate();
        }

        monitor.Reset();
    }

    private static async Task EmitOpenResetTransition()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
        });

        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        monitor.Reset();
    }

    private readonly record struct TelemetrySnapshot(
        string? EventName,
        int AttemptNumber,
        string? OperationKey,
        string? ContextOperationKey);

    private sealed class CallbackTelemetryListener(Action<KevlarTelemetryEvent> callback)
        : IKevlarTelemetryListener
    {
        public void OnEvent(in KevlarTelemetryEvent telemetryEvent) => callback(telemetryEvent);
    }

    private sealed class EqualTelemetryListener(Action callback) : IKevlarTelemetryListener
    {
        public void OnEvent(in KevlarTelemetryEvent telemetryEvent) => callback();

        public override bool Equals(object? obj) => obj is EqualTelemetryListener;

        public override int GetHashCode() => 0;
    }

    private sealed class TelemetryStrategy : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            context.RecordEvent("custom_strategy_event");
            return next.InvokeAsync(context);
        }
    }

    private sealed class BlockingFirstTimestampTimeProvider : TimeProvider, IDisposable
    {
        private readonly ManualResetEventSlim _sampleCaptured = new();
        private readonly ManualResetEventSlim _releaseSample = new();
        private int _getTimestampCalls;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            if (Interlocked.Increment(ref _getTimestampCalls) == 1)
            {
                _sampleCaptured.Set();
                _releaseSample.Wait();
            }

            return 0;
        }

        public bool WaitForBlockedSample(TimeSpan timeout) => _sampleCaptured.Wait(timeout);

        public void ReleaseBlockedSample() => _releaseSample.Set();

        public void Dispose()
        {
            _sampleCaptured.Dispose();
            _releaseSample.Dispose();
        }
    }
}

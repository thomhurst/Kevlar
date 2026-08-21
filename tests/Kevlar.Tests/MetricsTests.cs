using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

/// <summary>
/// Guards the built-in metrics: every shield publishes counters through the "Kevlar" meter with
/// zero configuration. Assertions filter on a per-test <c>shield.name</c> tag so parallel tests
/// cannot cross-contaminate counts.
/// </summary>
public class MetricsTests
{
    private sealed class KevlarMeterListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, Instrument> _instruments = new(StringComparer.Ordinal);
        private readonly ConcurrentBag<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = [];
        private readonly ConcurrentBag<(string Instrument, double Value, Dictionary<string, object?> Tags)> _doubleMeasurements = [];

        public KevlarMeterListener()
        {
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
                _measurements.Add((instrument.Name, value, CaptureTags(tags)));
            });
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                _doubleMeasurements.Add((instrument.Name, value, CaptureTags(tags)));
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

        public IReadOnlyCollection<long> Values(string instrument, string? shieldName, bool requireName = true) =>
            _measurements
                .Where(measurement => measurement.Instrument == instrument)
                .Where(measurement => HasName(measurement.Tags, shieldName, requireName))
                .Select(measurement => measurement.Value)
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
            ["kevlar.execution.duration"] = "s",
            ["kevlar.circuit_breaker.state"] = "{state}",
            ["kevlar.concurrency_limit.inflight"] = "{execution}",
            ["kevlar.concurrency_limit.queued"] = "{execution}",
            ["kevlar.concurrency_limit.capacity"] = "{execution}",
            ["kevlar.rate_limit.available"] = "{permit}",
            ["kevlar.rate_limit.queued"] = "{execution}",
        };

        await Assert.That(listener.Instruments.Select(instrument => instrument.Name))
            .IsEquivalentTo(expectedInstruments.Keys);
        await Assert.That(listener.Instruments.Single(instrument => instrument.Name == "kevlar.execution.duration"))
            .IsTypeOf<Histogram<double>>();
        await Assert.That(listener.Instruments
            .Where(instrument => instrument.Name is not "kevlar.execution.duration")
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
        var rateLimit = Shield.RateLimit(1, TimeSpan.FromHours(1)).WithName("metrics-reject-rate");
        await rateLimit.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(async () => await rateLimit.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();

        var circuit = Shield.CircuitBreaker(1, TimeSpan.FromHours(1)).WithName("metrics-reject-circuit");
        _ = await circuit.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await Assert.That(async () => await circuit.ExecuteAsync(_ => new ValueTask<int>(1)))
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
        try
        {
            await Assert.That(async () => await concurrency.ExecuteAsync(_ => new ValueTask<int>(2)))
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
    }

    [Test]
    public async Task Nested_Strategies_Record_One_Execution_And_Exact_Events()
    {
        using var listener = new KevlarMeterListener();
        const string name = "metrics-nested";
        var attempts = 0;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Fallback(42)
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
            .Fallback(-1)
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
        var shield = Shield.Hedge(2, TimeSpan.Zero).WithName("metrics-hedge");

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(listener.Total("kevlar.hedges", "metrics-hedge")).IsEqualTo(1);
    }

    [Test]
    public async Task Suppressed_Hedged_Attempts_Are_Not_Counted()
    {
        using var listener = new KevlarMeterListener();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
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
        var named = Shield.Empty.WithName("metrics-duration");

        await named.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(async () => await named.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Shield.Empty.ExecuteAsync(_ => new ValueTask<int>(2));

        var namedValues = listener.DoubleValues("kevlar.execution.duration", "metrics-duration");
        var namedTags = listener.DoubleMeasurements("kevlar.execution.duration", "metrics-duration");
        await Assert.That(namedValues.Count).IsEqualTo(2);
        await Assert.That(namedValues.All(value => value >= 0)).IsTrue();
        await Assert.That(namedTags.Select(tags => (string)tags["kevlar.execution.outcome"]!))
            .IsEquivalentTo(["success", "failure"]);
        await Assert.That(listener.DoubleValues("kevlar.execution.duration", null, requireName: false).Count >= 1)
            .IsTrue();
    }

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
        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await shield.ExecuteAsync(_ => new ValueTask<int>(2));
        monitor.Isolate();
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
        var closedMeasurements = listener.Values(
            "kevlar.circuit_breaker.state",
            "metrics-manual-circuit-state").Count(value => value == 0);

        monitor.Isolate();
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-manual-circuit-state"))
            .Contains(3);

        monitor.Reset();
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-manual-circuit-state").Count(value => value == 0))
            .IsEqualTo(closedMeasurements + 1);
    }

    [Test]
    public async Task Immediately_Admitted_Execution_Is_Not_Reported_As_Queued()
    {
        using var listener = new KevlarMeterListener();
        var shield = Shield.ConcurrencyLimit(1).WithName("metrics-immediate-concurrency");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);

        var queued = listener.Values(
            "kevlar.concurrency_limit.queued",
            "metrics-immediate-concurrency");
        await Assert.That(queued.Count).IsGreaterThan(0);
        await Assert.That(queued.All(value => value == 0)).IsTrue();
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
        var queued = shield.ExecuteAsync(_ => new ValueTask<int>(2), cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        release.SetResult();
        _ = await occupying;

        await Assert.That(listener.Values("kevlar.concurrency_limit.inflight", "metrics-concurrency-state"))
            .Contains(1)
            .And.Contains(0);
        await Assert.That(listener.Values("kevlar.concurrency_limit.queued", "metrics-concurrency-state"))
            .Contains(1)
            .And.Contains(0);
        await Assert.That(listener.Values("kevlar.concurrency_limit.capacity", "metrics-concurrency-state")
            .All(value => value == 1)).IsTrue();

        await Shield.ConcurrencyLimit(1).ExecuteAsync(_ => new ValueTask<int>(3));
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
        await shield.ExecuteAsync(_ => new ValueTask<int>(2));
        var queued = shield.ExecuteAsync(_ => new ValueTask<int>(3), cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();

        await Assert.That(listener.Values("kevlar.rate_limit.available", "metrics-rate-state"))
            .Contains(1)
            .And.Contains(0);
        await Assert.That(listener.Values("kevlar.rate_limit.queued", "metrics-rate-state"))
            .Contains(1)
            .And.Contains(0);
    }

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
}

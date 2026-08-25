using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Kevlar.Internal;
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
            ["kevlar.execution.duration"] = "s",
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
    public async Task Provisional_Unnamed_Circuit_Series_Stays_Current()
    {
        using var listener = new KevlarMeterListener();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options => options.Monitor = monitor)
            .WithName("metrics-provisional-circuit-name");

        monitor.Isolate();
        _ = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        monitor.Reset();

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
        _ = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        monitor.Reset();

        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                shieldName: null,
                requireName: false).Count)
            .IsEqualTo(0);
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

        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-circuit-alias-first").Last())
            .IsEqualTo(3);
        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-circuit-alias-second").Last())
            .IsEqualTo(3);

        monitor.Reset();
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
    public async Task Circuit_Execution_Sample_Cannot_Overwrite_A_Newer_Transition()
    {
        CircuitBreakerMonitor? monitor = null;
        var openMeasurements = 0;
        using var listener = new KevlarMeterListener((instrument, value) =>
        {
            if (instrument == "kevlar.circuit_breaker.state"
                && value == 1
                && Interlocked.Increment(ref openMeasurements) == 2)
            {
                monitor!.Reset();
            }
        });
        monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.Monitor = monitor;
        }).WithName("metrics-circuit-transition-race");

        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(listener.Values(
                "kevlar.circuit_breaker.state",
                "metrics-circuit-transition-race").Last())
            .IsEqualTo(0);
    }

    [Test]
    public async Task Circuit_Metric_Failure_Releases_An_Admitted_Probe()
    {
        var halfOpenMeasurements = 0;
        var metricsFailure = new InvalidOperationException("metrics callback");
        using var listener = new KevlarMeterListener((instrument, value) =>
        {
            if (instrument == "kevlar.circuit_breaker.state"
                && value == 2
                && Interlocked.Increment(ref halfOpenMeasurements) == 2)
            {
                throw metricsFailure;
            }
        });
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(1))
            .WithTimeProvider(timeProvider)
            .WithName("metrics-circuit-probe-failure");

        _ = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();

        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(2))).IsEqualTo(2);
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

        await Assert.That(listener.Values(
                "kevlar.concurrency_limit.inflight",
                "metrics-concurrency-handoffs").All(value => value <= 1))
            .IsTrue();
    }

    [Test]
    public async Task Concurrency_Metric_Failure_Releases_The_Pending_Wait()
    {
        var throwOnQueued = true;
        var metricsFailure = new InvalidOperationException("metrics callback");
        using var observer = new KevlarMeterListener();
        using var listener = new KevlarMeterListener((instrument, value) =>
        {
            if (throwOnQueued && instrument == "kevlar.concurrency_limit.queued" && value == 1)
            {
                throwOnQueued = false;
                throw metricsFailure;
            }
        });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.ConcurrencyLimit(1, 1)
            .WithName("metrics-concurrency-pending-failure");
        var holder = shield.ExecuteAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }).AsTask();
        await entered.Task;

        var failed = shield.ExecuteAsync(_ => ValueTask.CompletedTask).AsTask();
        InvalidOperationException? thrown;
        try
        {
            thrown = await Assert.That(async () =>
                    await failed.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<InvalidOperationException>();
            await Assert.That(observer.Values(
                    "kevlar.concurrency_limit.queued",
                    "metrics-concurrency-pending-failure").Last())
                .IsEqualTo(0);
        }
        finally
        {
            release.TrySetResult();
            await holder;
        }

        await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
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
        await Assert.That(listener.Values(
                "kevlar.rate_limit.queued",
                "metrics-concurrent-rate-cancellation"))
            .Contains(2);

        await Task.WhenAll(
            Task.Run(firstCancellation.Cancel),
            Task.Run(secondCancellation.Cancel));
        await Assert.That(async () => await first).Throws<OperationCanceledException>();
        await Assert.That(async () => await second).Throws<OperationCanceledException>();

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
    public async Task Rate_Metric_Failure_Removes_The_Queued_Reservation()
    {
        var throwOnQueued = true;
        var metricsFailure = new InvalidOperationException("metrics callback");
        using var observer = new KevlarMeterListener();
        using var listener = new KevlarMeterListener((instrument, value) =>
        {
            if (throwOnQueued && instrument == "kevlar.rate_limit.queued" && value == 1)
            {
                throwOnQueued = false;
                throw metricsFailure;
            }
        });
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromHours(1);
            options.QueueLimit = 1;
        }).WithTimeProvider(timeProvider).WithName("metrics-rate-reservation-failure");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => ValueTask.CompletedTask))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();
        await Assert.That(observer.Values(
                "kevlar.rate_limit.queued",
                "metrics-rate-reservation-failure").Last())
            .IsEqualTo(0);

        var queued = shield.ExecuteAsync(_ => ValueTask.CompletedTask, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Rate_Metric_Failure_Restores_An_Immediate_Permit()
    {
        var throwOnAvailable = true;
        var metricsFailure = new InvalidOperationException("metrics callback");
        using var observer = new KevlarMeterListener();
        using var listener = new KevlarMeterListener((instrument, value) =>
        {
            if (throwOnAvailable && instrument == "kevlar.rate_limit.available" && value == 0)
            {
                throwOnAvailable = false;
                throw metricsFailure;
            }
        });
        var invoked = false;
        var shield = Shield.RateLimit(1, TimeSpan.FromHours(1))
            .WithName("metrics-rate-immediate-failure");

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync(_ =>
                {
                    invoked = true;
                    return ValueTask.CompletedTask;
                }))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();
        await Assert.That(invoked).IsFalse();
        await Assert.That(observer.Values(
                "kevlar.rate_limit.available",
                "metrics-rate-immediate-failure").Last())
            .IsEqualTo(1);

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
    }

    [Test]
    public async Task Rate_Metric_Failure_Preserves_A_Nested_Admission()
    {
        var timeProvider = new FakeTimeProvider();
        var nested = false;
        var nestedInvocations = 0;
        var metricsFailure = new InvalidOperationException("metrics callback");
        Shield? shield = null;
        using var listener = new KevlarMeterListener((instrument, value) =>
        {
            if (nested || instrument != "kevlar.rate_limit.available" || value != 1)
            {
                return;
            }

            nested = true;
            timeProvider.Advance(TimeSpan.FromHours(2));
            shield!.ExecuteAsync(_ =>
            {
                nestedInvocations++;
                return ValueTask.CompletedTask;
            }).GetAwaiter().GetResult();
            throw metricsFailure;
        });
        shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromHours(1);
            options.Burst = 2;
        }).WithTimeProvider(timeProvider).WithName("metrics-rate-nested-failure");

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => ValueTask.CompletedTask))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();
        await Assert.That(nestedInvocations).IsEqualTo(1);

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        _ = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => ValueTask.CompletedTask))
            .Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Rate_Metric_Failure_Preserves_Admission_After_Listener_Disables()
    {
        var nested = false;
        var nestedInvocations = 0;
        var metricsFailure = new InvalidOperationException("metrics callback");
        Shield? shield = null;
        KevlarMeterListener? listener = null;
        listener = new KevlarMeterListener((instrument, value) =>
        {
            if (nested || instrument != "kevlar.rate_limit.available" || value != 1)
            {
                return;
            }

            nested = true;
            listener!.Dispose();
            shield!.ExecuteAsync(_ =>
            {
                nestedInvocations++;
                return ValueTask.CompletedTask;
            }).GetAwaiter().GetResult();
            throw metricsFailure;
        });
        using (listener)
        {
            shield = Shield.RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromHours(1);
                options.Burst = 2;
            }).WithName("metrics-rate-disabled-nested-failure");

            var thrown = await Assert.That(async () =>
                    await shield.ExecuteAsync(_ => ValueTask.CompletedTask))
                .Throws<InvalidOperationException>();
            await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();
        }

        await Assert.That(nestedInvocations).IsEqualTo(1);
        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        _ = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => ValueTask.CompletedTask))
            .Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Rate_Metric_Rollback_Preserves_An_Admission_That_Observed_Metrics_Disabled()
    {
        using var timeProvider = new BlockingFirstTimestampTimeProvider();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromHours(1);
            options.Burst = 2;
        }).WithTimeProvider(timeProvider).WithName("metrics-rate-concurrent-enable");
        var untracked = Task.Run(async () => await shield.ExecuteAsync(_ => ValueTask.CompletedTask));

        await Assert.That(timeProvider.WaitForBlockedSample(TimeSpan.FromSeconds(5))).IsTrue();
        var metricsFailure = new InvalidOperationException("metrics callback");
        var throwOnce = true;
        using var listener = new KevlarMeterListener((instrument, _) =>
        {
            if (throwOnce && instrument == "kevlar.rate_limit.available")
            {
                throwOnce = false;
                throw metricsFailure;
            }
        });
        var failedAdmission = Task.Run(async () => await shield.ExecuteAsync(_ => ValueTask.CompletedTask));

        timeProvider.ReleaseBlockedSample();
        await untracked.WaitAsync(TimeSpan.FromSeconds(5));
        var thrown = await Assert.That(async () =>
                await failedAdmission.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        _ = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => ValueTask.CompletedTask))
            .Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Rate_Queue_Reports_Zero_Availability_After_Its_Due_Time()
    {
        var timeProvider = new FakeTimeProvider();
        var advancedWithQueuedReservation = false;
        var observedInvalidAvailability = false;
        using var listener = new KevlarMeterListener((instrument, value) =>
        {
            if (instrument == "kevlar.rate_limit.queued"
                && value == 1
                && !advancedWithQueuedReservation)
            {
                advancedWithQueuedReservation = true;
                timeProvider.Advance(TimeSpan.FromSeconds(1));
            }
            else if (instrument == "kevlar.rate_limit.available"
                && value > 0
                && advancedWithQueuedReservation)
            {
                observedInvalidAvailability = true;
            }
        });
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromSeconds(1);
            options.QueueLimit = 1;
        }).WithTimeProvider(timeProvider).WithName("metrics-rate-due-reservation");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);

        await Assert.That(advancedWithQueuedReservation).IsTrue();
        await Assert.That(observedInvalidAvailability).IsFalse();
    }

    [Test]
    public async Task Rate_Metric_Failure_Restores_A_Consumed_Queued_Permit()
    {
        var timeProvider = new FakeTimeProvider();
        var reservationQueued = false;
        var throwOnConsumption = true;
        var metricsFailure = new InvalidOperationException("metrics callback");
        using var listener = new KevlarMeterListener((instrument, value) =>
        {
            if (instrument != "kevlar.rate_limit.queued")
            {
                return;
            }

            if (value == 1 && !reservationQueued)
            {
                reservationQueued = true;
                timeProvider.Advance(TimeSpan.FromSeconds(1));
            }
            else if (value == 0 && reservationQueued && throwOnConsumption)
            {
                throwOnConsumption = false;
                throw metricsFailure;
            }
        });
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromSeconds(1);
            options.QueueLimit = 1;
        }).WithTimeProvider(timeProvider).WithName("metrics-rate-consumption-failure");

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => ValueTask.CompletedTask))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
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

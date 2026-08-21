using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Kevlar.Tests;

[NotInParallel]
public class StructuredTelemetryTests
{
    private static readonly KevlarKey<int> OperationId = new("operation-id");

    [Test]
    public async Task Execution_Lifecycle_Is_Typed_Ordered_And_Correlated()
    {
        var listener = new RecordingListener();
        using var subscription = KevlarDiagnostics.Subscribe(listener);
        var shield = Shield.Retry(0, Backoff.None).WithName("orders");

        var result = await shield.ExecuteWithContextAsync(
            42,
            static (value, properties) => properties.Set(OperationId, value),
            static (_, context) => new ValueTask<int>(context.Properties.GetOrDefault(OperationId)));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(listener.Events.Count).IsEqualTo(2);
        await Assert.That(listener.Events[0]).IsEqualTo(new RecordedEvent(
            KevlarEventKind.ExecutionStarted,
            KevlarEventSeverity.Debug,
            KevlarOutcomeClassification.None,
            "orders",
            -1,
            0,
            false,
            typeof(int),
            42));
        await Assert.That(listener.Events[1] with { Duration = TimeSpan.Zero }).IsEqualTo(new RecordedEvent(
            KevlarEventKind.ExecutionCompleted,
            KevlarEventSeverity.Information,
            KevlarOutcomeClassification.Success,
            "orders",
            -1,
            0,
            true,
            typeof(int),
            42));
        await Assert.That(listener.Events[1].Duration).IsGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Failure_And_PreCancellation_Are_Classified()
    {
        var listener = new RecordingListener();
        using var subscription = KevlarDiagnostics.Subscribe(listener);
        var failure = new InvalidOperationException("failure");
        var initialized = false;

        var failed = await Shield.Empty.ExecuteOutcomeAsync<int>(_ => throw failure);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = await Shield.Empty.ExecuteOutcomeAsync<int>(
            static _ => new ValueTask<int>(42),
            cancellation.Token);
        await Assert.That(async () => await Shield.Empty.ExecuteWithContextAsync(
                42,
                (_, _) => initialized = true,
                static (_, _) => new ValueTask<int>(42),
                cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(ReferenceEquals(failed.Exception, failure)).IsTrue();
        await Assert.That(canceled.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(initialized).IsFalse();
        await Assert.That(listener.Events
            .Where(static item => item.Kind == KevlarEventKind.ExecutionCompleted)
            .Select(static item => item.Outcome)
            .ToArray()).IsEquivalentTo([
                KevlarOutcomeClassification.Failure,
                KevlarOutcomeClassification.Canceled,
                KevlarOutcomeClassification.Canceled,
            ]);
    }

    [Test]
    public async Task Listener_Faults_Do_Not_Change_Execution_Or_Block_Other_Listeners()
    {
        var faulting = new CallbackListener(static _ => throw new InvalidOperationException("listener"));
        var recording = new RecordingListener();
        using var first = KevlarDiagnostics.Subscribe(faulting);
        using var second = KevlarDiagnostics.Subscribe(recording);

        var result = await Shield.Empty.ExecuteAsync(static _ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(faulting.Calls).IsEqualTo(2);
        await Assert.That(recording.Events.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Subscriptions_Are_Reentrant_Concurrent_And_Disposable()
    {
        var nested = 0;
        var order = new ConcurrentQueue<KevlarEventKind>();
        var listener = new CallbackListener(kind =>
        {
            order.Enqueue(kind);
            if (kind == KevlarEventKind.ExecutionStarted
                && Interlocked.CompareExchange(ref nested, 1, 0) == 0)
            {
                _ = Shield.Empty.Execute(static _ => 1);
            }
        });
        var subscription = KevlarDiagnostics.Subscribe(listener);

        _ = Shield.Empty.Execute(static _ => 1);
        await Assert.That(order.ToArray()).IsEquivalentTo([
            KevlarEventKind.ExecutionStarted,
            KevlarEventKind.ExecutionStarted,
            KevlarEventKind.ExecutionCompleted,
            KevlarEventKind.ExecutionCompleted,
        ]);

        Parallel.For(0, 100, static _ => Shield.Empty.Execute(static _ => 1));
        subscription.Dispose();
        _ = Shield.Empty.Execute(static _ => 1);

        await Assert.That(listener.Calls).IsEqualTo(204);
    }

    [Test]
    public async Task Listener_Filter_And_Null_Guard_Are_Immediate()
    {
        var listener = new RecordingListener(KevlarEventKind.ExecutionCompleted);
        using var subscription = KevlarDiagnostics.Subscribe(listener);

        _ = Shield.Empty.Execute(static _ => 1);

        await Assert.That(listener.Events.Select(static item => item.Kind).ToArray())
            .IsEquivalentTo([KevlarEventKind.ExecutionCompleted]);
        await Assert.That(() => KevlarDiagnostics.Subscribe(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Void_Executions_Use_The_Generic_Event_Path()
    {
        var listener = new RecordingListener();
        using var subscription = KevlarDiagnostics.Subscribe(listener);

        await Shield.Empty.ExecuteAsync(static _ => ValueTask.CompletedTask);

        await Assert.That(listener.Events.Count).IsEqualTo(2);
        await Assert.That(listener.Events[1].Outcome).IsEqualTo(KevlarOutcomeClassification.Success);
    }

    [Test]
    public async Task Structured_Events_Do_Not_Double_Count_Execution_Metrics()
    {
        var executions = 0L;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName
                    && instrument.Name == "kevlar.executions")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref executions, measurement));
        meterListener.Start();
        using var subscription = KevlarDiagnostics.Subscribe(new RecordingListener());

        _ = Shield.Empty.Execute(static _ => 1);
        _ = await Shield.Empty.ExecuteOutcomeAsync<int>(static _ =>
            throw new InvalidOperationException());

        await Assert.That(executions).IsEqualTo(2);
    }

    private sealed class RecordingListener(KevlarEventKind? enabledKind = null) : KevlarEventListener
    {
        public List<RecordedEvent> Events { get; } = [];

        public override bool IsEnabled(KevlarEventKind kind) => enabledKind is null || enabledKind == kind;

        public override void OnEvent<T>(in KevlarEvent<T> telemetryEvent)
        {
            var operationId = telemetryEvent.Context.Properties.GetOrDefault(OperationId);
            lock (Events)
            {
                Events.Add(new RecordedEvent(
                    telemetryEvent.Kind,
                    telemetryEvent.Severity,
                    telemetryEvent.OutcomeClassification,
                    telemetryEvent.ShieldName,
                    telemetryEvent.StrategyIndex,
                    telemetryEvent.Attempt,
                    telemetryEvent.HasOutcome,
                    typeof(T),
                    operationId)
                {
                    Duration = telemetryEvent.Duration,
                });
            }
        }
    }

    private sealed class CallbackListener(Action<KevlarEventKind> callback) : KevlarEventListener
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public override void OnEvent<T>(in KevlarEvent<T> telemetryEvent)
        {
            Interlocked.Increment(ref _calls);
            callback(telemetryEvent.Kind);
        }
    }

    private sealed record RecordedEvent(
        KevlarEventKind Kind,
        KevlarEventSeverity Severity,
        KevlarOutcomeClassification Outcome,
        string? ShieldName,
        int StrategyIndex,
        int Attempt,
        bool HasOutcome,
        Type ResultType,
        int OperationId)
    {
        public TimeSpan Duration { get; init; }
    }
}

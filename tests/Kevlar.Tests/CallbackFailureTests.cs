using System.Diagnostics.Metrics;

namespace Kevlar.Tests;

public class CallbackFailureTests
{
    [Test]
    [NotInParallel]
    public async Task Throwing_Callback_Is_Reported_Without_Changing_Successful_Outcome()
    {
        var callbackFailure = new IOException("callback");
        var shieldName = $"callback-{Guid.NewGuid():N}";
        var reported = new List<CallbackErrorEvent>();
        var measurements = new List<string>();
        Action<CallbackErrorEvent> throwingHandler = _ => throw new ApplicationException("diagnostics");
        Action<CallbackErrorEvent> recordingHandler = reported.Add;
        using var listener = CreateCallbackErrorListener(shieldName, measurements);
        KevlarDiagnostics.OnCallbackError += throwingHandler;
        KevlarDiagnostics.OnCallbackError += recordingHandler;

        try
        {
            var attempts = 0;
            var shield = Shield.Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = _ => throw callbackFailure;
            }).WithName(shieldName);

            var result = await shield.ExecuteAsync(_ => new ValueTask<int>(
                ++attempts == 1 ? throw new HttpRequestException("transient") : 42));

            await Assert.That(result).IsEqualTo(42);
            await Assert.That(reported.Count).IsEqualTo(1);
            await Assert.That(reported[0].Kind).IsEqualTo(CallbackErrorKind.Retry);
            await Assert.That(reported[0].ShieldName).IsEqualTo(shieldName);
            await Assert.That(reported[0].StrategyIndex).IsEqualTo(0);
            await Assert.That(ReferenceEquals(reported[0].Exception, callbackFailure)).IsTrue();
            await Assert.That(measurements).IsEquivalentTo(["retry"]);
        }
        finally
        {
            KevlarDiagnostics.OnCallbackError -= recordingHandler;
            KevlarDiagnostics.OnCallbackError -= throwingHandler;
        }
    }

    [Test]
    [NotInParallel]
    public async Task Faulted_Async_Callback_Is_Awaited_And_Does_Not_Replace_Failed_Outcome()
    {
        var operationFailure = new HttpRequestException("operation");
        var callbackFailure = new IOException("callback");
        var callbackSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CallbackErrorEvent? reported = null;
        Action<CallbackErrorEvent> handler = item => reported = item;
        KevlarDiagnostics.OnCallbackError += handler;

        try
        {
            var shield = Shield.Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetryAsync = _ => new ValueTask(callbackSource.Task);
            });
            var execution = shield.ExecuteOutcomeAsync<int>(_ => throw operationFailure).AsTask();

            await Task.Yield();
            await Assert.That(execution.IsCompleted).IsFalse();
            callbackSource.SetException(callbackFailure);
            var outcome = await execution;

            await Assert.That(ReferenceEquals(outcome.Exception, operationFailure)).IsTrue();
            await Assert.That(reported?.Kind).IsEqualTo(CallbackErrorKind.Retry);
            await Assert.That(ReferenceEquals(reported?.Exception, callbackFailure)).IsTrue();
        }
        finally
        {
            KevlarDiagnostics.OnCallbackError -= handler;
        }
    }

    [Test]
    [NotInParallel]
    public async Task Sync_Execute_Preserves_Result_When_Callback_Throws()
    {
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => throw new IOException("callback");
        });

        var result = shield.Execute(_ => ++attempts == 1
            ? throw new HttpRequestException("transient")
            : 42);

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    [NotInParallel]
    public async Task Throwing_Monitor_Subscriber_Does_Not_Block_Other_Subscribers_Or_Controls()
    {
        var monitor = new CircuitBreakerMonitor();
        var observed = new List<CircuitState>();
        monitor.StateChanged += _ => throw new IOException("observer");
        monitor.StateChanged += change => observed.Add(change.To);
        _ = Shield.CircuitBreaker(options =>
        {
            options.Monitor = monitor;
            options.OnStateChanged = _ => throw new ApplicationException("option observer");
        });

        await monitor.IsolateAsync();
        await monitor.ResetAsync();

        await Assert.That(observed).IsEquivalentTo(
            [CircuitState.Isolated, CircuitState.Closed],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    private static MeterListener CreateCallbackErrorListener(
        string shieldName,
        List<string> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, activeListener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName
                    && instrument.Name == "kevlar.callback_errors")
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? observedShield = null;
            string? kind = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "kevlar.shield.name")
                {
                    observedShield = tag.Value?.ToString();
                }
                else if (tag.Key == "kevlar.callback.kind")
                {
                    kind = tag.Value?.ToString();
                }
            }

            if (string.Equals(observedShield, shieldName, StringComparison.Ordinal)
                && kind is not null)
            {
                measurements.Add(kind);
            }
        });
        listener.Start();
        return listener;
    }
}

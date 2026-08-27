using System.Diagnostics.Metrics;

namespace Kevlar.Tests;

public class CallbackFailureTests
{
    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Throwing_Handling_Predicate_Is_Reported_Without_Replacing_The_Outcome(
        bool contextAware)
    {
        var predicateFailure = new IOException("predicate");
        var executionFailure = new ArgumentException("execution");
        var shieldName = $"predicate-{Guid.NewGuid():N}";
        var reported = new List<CallbackErrorEvent>();
        var measurements = new List<(string Kind, string Source)>();
        Action<CallbackErrorEvent> handler = reported.Add;
        using var listener = CreateCallbackErrorListener(shieldName, measurements);
        KevlarDiagnostics.OnCallbackError += handler;

        try
        {
            var shield = contextAware
                ? Shield.WhenContext((HandlingEvent _) => throw predicateFailure)
                    .Retry(1, Backoff.None)
                : Shield.When(_ => throw predicateFailure)
                    .Retry(1, Backoff.None);
            shield = shield.WithName(shieldName);

            var thrown = await Assert.That(async () => await shield.ExecuteAsync<int>(
                _ => throw executionFailure)).Throws<ArgumentException>();

            await Assert.That(thrown).IsSameReferenceAs(executionFailure);
            await Assert.That(reported).HasSingleItem();
            await Assert.That(reported[0].Kind).IsEqualTo(CallbackErrorKind.HandlingPredicate);
            await Assert.That(reported[0].Source).IsEqualTo("HandlingPredicate");
            await Assert.That(reported[0].Exception).IsSameReferenceAs(predicateFailure);
            await Assert.That(measurements)
                .IsEquivalentTo([("handling_predicate", "HandlingPredicate")]);
        }
        finally
        {
            KevlarDiagnostics.OnCallbackError -= handler;
        }
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Throwing_Result_Predicate_Is_Reported_Without_Replacing_The_Result(
        bool contextAware)
    {
        var predicateFailure = new IOException("predicate");
        var shieldName = $"result-predicate-{Guid.NewGuid():N}";
        var reported = new List<CallbackErrorEvent>();
        var measurements = new List<(string Kind, string Source)>();
        Action<CallbackErrorEvent> handler = reported.Add;
        using var listener = CreateCallbackErrorListener(shieldName, measurements);
        KevlarDiagnostics.OnCallbackError += handler;

        try
        {
            var shield = contextAware
                ? Shield.For<int>()
                    .WhenResultContext((HandlingEvent<int> _) => throw predicateFailure)
                    .FallbackTo(-1)
                : Shield.For<int>()
                    .WhenResult(_ => throw predicateFailure)
                    .FallbackTo(-1);
            shield = shield.WithName(shieldName);

            var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

            await Assert.That(result).IsEqualTo(42);
            await Assert.That(reported).HasSingleItem();
            await Assert.That(reported[0].Kind).IsEqualTo(CallbackErrorKind.HandlingPredicate);
            await Assert.That(reported[0].Source).IsEqualTo("HandlingPredicate");
            await Assert.That(reported[0].Exception).IsSameReferenceAs(predicateFailure);
            await Assert.That(measurements)
                .IsEquivalentTo([("handling_predicate", "HandlingPredicate")]);
        }
        finally
        {
            KevlarDiagnostics.OnCallbackError -= handler;
        }
    }

    [Test]
    [NotInParallel]
    public async Task Throwing_Callback_Is_Reported_Without_Changing_Successful_Outcome()
    {
        var callbackFailure = new IOException("callback");
        var shieldName = $"callback-{Guid.NewGuid():N}";
        var reported = new List<CallbackErrorEvent>();
        var measurements = new List<(string Kind, string Source)>();
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
            await Assert.That(reported[0].Source).IsEqualTo("RetryOptions.OnRetry");
            await Assert.That(reported[0].ShieldName).IsEqualTo(shieldName);
            await Assert.That(reported[0].StrategyIndex).IsEqualTo(0);
            await Assert.That(ReferenceEquals(reported[0].Exception, callbackFailure)).IsTrue();
            await Assert.That(measurements).IsEquivalentTo([("retry", "RetryOptions.OnRetry")]);
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
                options.OnRetry = _ => new ValueTask(callbackSource.Task);
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
    public async Task Sync_Execute_Reports_Synchronously_Faulted_Hook_Without_Changing_Result()
    {
        var callbackFailure = new IOException("callback");
        CallbackErrorEvent? reported = null;
        Action<CallbackErrorEvent> handler = item => reported = item;
        KevlarDiagnostics.OnCallbackError += handler;

        try
        {
            var attempts = 0;
            var shield = Shield.Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = _ => ValueTask.FromException(callbackFailure);
            });

            var result = shield.Execute(_ => ++attempts == 1
                ? throw new HttpRequestException("transient")
                : 42);

            await Assert.That(result).IsEqualTo(42);
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
    public async Task Throwing_Strategy_Event_Metrics_Do_Not_Escape_Callback_Error_Reporting()
    {
        using var listener = new MeterListener
        {
            InstrumentPublished = static (instrument, activeListener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName
                    && instrument.Name == "kevlar.strategy.events")
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(static (_, _, _, _) =>
            throw new InvalidOperationException("metrics"));
        listener.Start();

        var result = Shield.Empty.ExecuteWithContext(context =>
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.Retry,
                context,
                new IOException("callback"),
                "test.callback");
            return 42;
        });

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
        List<(string Kind, string Source)> measurements)
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
            string? source = null;
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
                else if (tag.Key == "kevlar.callback.source")
                {
                    source = tag.Value?.ToString();
                }
            }

            if (string.Equals(observedShield, shieldName, StringComparison.Ordinal)
                && kind is not null
                && source is not null)
            {
                measurements.Add((kind, source));
            }
        });
        listener.Start();
        return listener;
    }
}

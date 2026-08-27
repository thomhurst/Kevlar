using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Testing.Tests;

[NotInParallel]
public class TelemetryRecorderTests
{
    [Test]
    public async Task Exposes_Timeout_Generator_Recorder_Overload()
    {
        var overload = typeof(TelemetryRecorder).GetMethod(
            nameof(TelemetryRecorder.Record),
            [typeof(TimeoutEvent), typeof(TimeSpan)]);

        await Assert.That(overload).IsNotNull();
        await Assert.That(overload!.ReturnType).IsEqualTo(typeof(ValueTask<TimeSpan>));

        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var shield = Shield.Timeout(options =>
            options.TimeoutGenerator = timeout =>
                recorder.Record(timeout, TimeSpan.FromMinutes(1)))
            .WithName("generated-timeout");

        await Assert.That(shield.Execute(static _ => 42)).IsEqualTo(42);
        var record = recorder.Callbacks.Single();
        await Assert.That(record.Kind).IsEqualTo(CallbackKind.Timeout);
        await Assert.That(record.ShieldName).IsEqualTo("generated-timeout");
        await Assert.That(record.Timeout).IsEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task Records_Typed_Telemetry_Events_Without_Retaining_Context()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.Name = "testing-retry";
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
        }).WithName("testing-events");

        _ = await shield.ExecuteWithContextAsync(
            "checkout",
            static (operation, properties) => properties.Set(KevlarKeys.OperationKey, operation),
            (_, _) => ++attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42));

        await recorder.WaitForEventCountAsync(3).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(recorder.Events.Select(item => item.EventName).SequenceEqual(
            ["execution_attempt", "retry", "execution_attempt"])).IsTrue();
        await Assert.That(recorder.Events.All(item =>
            item.StrategyName == "testing-retry"
            && item.ShieldName == "testing-events"
            && item.OperationKey == "checkout")).IsTrue();
    }

    [Test]
    public async Task Records_Winning_Primary_And_Cancelled_Hedge_Outcomes()
    {
        using var recorder = new TelemetryRecorder();
        var name = $"hedge-outcomes-{Guid.NewGuid():N}";
        var primaryRelease = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hedgeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var shield = Shield.Hedge(1, TimeSpan.Zero)
            .WithName(name)
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(token => Interlocked.Increment(ref attempts) == 1
            ? new ValueTask<int>(primaryRelease.Task)
            : WaitForCancellationAsync(token, hedgeStarted)).AsTask();

        await hedgeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        primaryRelease.SetResult(42);
        await Assert.That(await execution).IsEqualTo(42);
        await recorder.WaitForEventCountAsync(3).WaitAsync(TimeSpan.FromSeconds(5));
        await recorder.WaitForMetricCountAsync(3).WaitAsync(TimeSpan.FromSeconds(5));

        var events = recorder.Events
            .Where(item => item.EventName == "hedge_attempt" && item.ShieldName == name)
            .OrderBy(item => item.AttemptNumber)
            .ToArray();
        await Assert.That(events.Length).IsEqualTo(2);
        await Assert.That(events[0].AttemptNumber).IsEqualTo(0);
        await Assert.That(events[0].IsWinner).IsTrue();
        await Assert.That(events[0].IsSuccess).IsTrue();
        await Assert.That(events[0].IsCancelled).IsFalse();
        await Assert.That(events[1].AttemptNumber).IsEqualTo(1);
        await Assert.That(events[1].IsWinner).IsFalse();
        await Assert.That(events[1].IsSuccess).IsFalse();
        await Assert.That(events[1].IsCancelled).IsTrue();
        await Assert.That(events[1].Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(events.All(item => item.Duration == TimeSpan.FromMilliseconds(250))).IsTrue();

        var results = recorder.Metrics
            .Where(item => item.InstrumentName == "kevlar.hedge_attempts"
                && Equals(item.Tags["kevlar.shield.name"], name))
            .Select(item => (string)item.Tags["result"]!)
            .ToArray();
        await Assert.That(results).IsEquivalentTo(["won", "cancelled"]);
    }

    [Test]
    public async Task Records_Synchronously_Completed_Primary_Winner_Before_Hedge_Delay()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var name = $"fast-primary-{Guid.NewGuid():N}";
        var shield = Shield.Hedge(1, TimeSpan.FromSeconds(1)).WithName(name);

        var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(42));

        var attempt = recorder.Events.Single(item =>
            item.EventName == "hedge_attempt" && item.ShieldName == name);
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempt.AttemptNumber).IsEqualTo(0);
        await Assert.That(attempt.IsWinner).IsTrue();
        await Assert.That(attempt.IsSuccess).IsTrue();
        await Assert.That(attempt.IsCancelled).IsFalse();
    }

    [Test]
    public async Task Records_Each_Failed_Hedge_Attempt_Without_Changing_The_Final_Exception()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var name = $"failed-hedges-{Guid.NewGuid():N}";
        var attempts = 0;
        var outcome = await Shield.Hedge(1, TimeSpan.Zero)
            .WithName(name)
            .ExecuteOutcomeAsync<int>(_ => ValueTask.FromException<int>(
                new InvalidOperationException($"attempt-{Interlocked.Increment(ref attempts)}")));

        await recorder.WaitForEventCountAsync(3).WaitAsync(TimeSpan.FromSeconds(5));
        var events = recorder.Events
            .Where(item => item.EventName == "hedge_attempt" && item.ShieldName == name)
            .OrderBy(item => item.AttemptNumber)
            .ToArray();

        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(outcome.Exception!.Message).IsEqualTo("attempt-2");
        await Assert.That(events.Length).IsEqualTo(2);
        await Assert.That(events.All(item => item.Exception is InvalidOperationException)).IsTrue();
        await Assert.That(events[0].IsWinner).IsFalse();
        await Assert.That(events[1].IsWinner).IsTrue();
    }

    [Test]
    public async Task Records_Metrics_With_Documented_Attributes()
    {
        using var recorder = new TelemetryRecorder();
        var named = Shield.Empty.WithName("orders");

        await named.ExecuteAsync(static _ => ValueTask.CompletedTask);
        await Shield.Empty.ExecuteOutcomeAsync<int>(
            static _ => ValueTask.FromException<int>(new InvalidOperationException()));
        await recorder.WaitForMetricCountAsync(4).WaitAsync(TimeSpan.FromSeconds(5));

        var executions = recorder.Metrics
            .Where(record => record.InstrumentName == "kevlar.executions")
            .ToArray();
        await Assert.That(executions.Length).IsEqualTo(2);
        await Assert.That(executions[0].Tags["kevlar.shield.name"]).IsEqualTo("orders");
        await Assert.That(executions[0].Tags["kevlar.execution.outcome"]).IsEqualTo("success");
        await Assert.That(executions[1].Tags.ContainsKey("kevlar.shield.name")).IsFalse();
        await Assert.That(executions[1].Tags["kevlar.execution.outcome"]).IsEqualTo("failure");
    }

#if NET9_0_OR_GREATER
    [Test]
    public async Task Metric_Wait_Collects_Observable_Gauges()
    {
        using var recorder = new TelemetryRecorder();
        var baseline = recorder.Metrics.Count;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = Shield.ConcurrencyLimit(1)
            .WithName("observable-wait")
            .ExecuteAsync(async _ =>
            {
                started.SetResult();
                await release.Task;
            }).AsTask();
        await started.Task;

        try
        {
            await recorder.WaitForMetricCountAsync(baseline + 1)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(recorder.Metrics.Any(metric =>
                    metric.InstrumentName == "kevlar.concurrency_limit.inflight"
                    && metric.Value == 1
                    && Equals(metric.Tags["kevlar.shield.name"], "observable-wait")))
                .IsTrue();
        }
        finally
        {
            release.TrySetResult();
        }

        await execution;
    }
#endif

    [Test]
    public async Task Records_Typed_And_Untyped_Callbacks_In_Order()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var typed = Shield.For<int>()
            .WhenResult(static result => result < 0)
            .FallbackTo(42, options => options.OnFallback = fallback =>
            {
                recorder.Record(fallback);
                return default;
            })
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    recorder.Record(retry);
                    return default;
                };
            })
            .WithName("typed");
        var attempts = 0;

        var result = await typed.ExecuteAsync(_ => new ValueTask<int>(
            Interlocked.Increment(ref attempts) == 1 ? -1 : -2));

        await Assert.That(result).IsEqualTo(42);
        var records = recorder.Callbacks;
        await Assert.That(records.Select(record => record.Kind)
            .SequenceEqual([CallbackKind.Retry, CallbackKind.Fallback])).IsTrue();
        await Assert.That(records[0].AttemptNumber).IsEqualTo(0);
        await Assert.That(records[0].Result).IsEqualTo(-1);
        await Assert.That(records[0].ShieldName).IsEqualTo("typed");
        await Assert.That(records[1].Result).IsEqualTo(-2);

        var untyped = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry =>
            {
                recorder.Record(retry);
                return default;
            };
        });
        await untyped.ExecuteOutcomeAsync<int>(static _ => throw new ApplicationException("failure"));

        await Assert.That(recorder.Callbacks[2].Exception).IsTypeOf<ApplicationException>();
        await Assert.That(recorder.Callbacks[2].ShieldName).IsNull();
    }

    [Test]
    public async Task Waiters_Observe_Concurrent_Records_And_Disposal_Stops_Metrics()
    {
        using var recorder = new TelemetryRecorder();
        var shields = Enumerable.Range(0, 8)
            .Select(index => Shield.Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    recorder.Record(retry);
                    return default;
                };
            }).WithName($"shield-{index}"))
            .ToArray();

        var wait = recorder.WaitForCallbackCountAsync(shields.Length);
        await Task.WhenAll(shields.Select(shield => shield.ExecuteOutcomeAsync<int>(
            static _ => throw new InvalidOperationException()).AsTask()));
        await wait.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(recorder.Callbacks.Count).IsEqualTo(shields.Length);
        await Assert.That(recorder.Callbacks.Select(record => record.ShieldName).Distinct().Count())
            .IsEqualTo(shields.Length);

        var metricCount = recorder.Metrics.Count;
        recorder.Dispose();
        await Shield.Empty.ExecuteAsync(static _ => ValueTask.CompletedTask);
        await Assert.That(recorder.Metrics.Count).IsEqualTo(metricCount);
    }

    [Test]
    public async Task Records_Timeout_Hedge_And_Circuit_Callbacks()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var timeout = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(1);
            options.OnTimeout = timeout =>
            {
                recorder.Record(timeout);
                return default;
            };
        }).WithName("timeout");
        await timeout.ExecuteOutcomeAsync<int>(static async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        });

        var hedge = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = hedge =>
            {
                recorder.Record(hedge);
                return default;
            };
        }).WithName("hedge");
        await hedge.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException()));

        var breaker = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.OnStateChanged = transition =>
            {
                recorder.Record(transition);
                return default;
            };
        });
        await breaker.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new ApplicationException("break")));

        var records = recorder.Callbacks;
        await Assert.That(records.Select(record => record.Kind).SequenceEqual(
            [CallbackKind.Timeout, CallbackKind.Hedge, CallbackKind.CircuitTransition])).IsTrue();
        await Assert.That(records[0].ShieldName).IsEqualTo("timeout");
        await Assert.That(records[0].Timeout).IsEqualTo(TimeSpan.FromMilliseconds(1));
        await Assert.That(records[1].ShieldName).IsEqualTo("hedge");
        await Assert.That(records[1].AttemptNumber).IsEqualTo(1);
        await Assert.That(records[2].From).IsEqualTo(CircuitState.Closed);
        await Assert.That(records[2].To).IsEqualTo(CircuitState.Open);
        await Assert.That(records[2].Exception).IsTypeOf<ApplicationException>();
    }

    [Test]
    public async Task Records_Typed_Hedge_Outcome()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var attempts = 0;
        var shield = Shield.For<string>()
            .WhenResult(static result => result == "retry")
            .Hedge(options =>
            {
                options.Delay = Timeout.InfiniteTimeSpan;
                options.OnHedge = recorder.Record;
            });

        var result = await shield.ExecuteAsync(_ =>
            new ValueTask<string>(Interlocked.Increment(ref attempts) == 1 ? "retry" : "success"));

        var record = recorder.Callbacks.Single();
        await Assert.That(result).IsEqualTo("success");
        await Assert.That(record.Kind).IsEqualTo(CallbackKind.Hedge);
        await Assert.That(record.AttemptNumber).IsEqualTo(1);
        await Assert.That(record.Result).IsEqualTo("retry");
        await Assert.That(record.Exception).IsNull();
    }

    [Test]
    public async Task Records_Circuit_Break_Duration_Statistics_And_Strategy_Index()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var shield = Shield.For<int>()
            .WhenResult(static result => result < 0)
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDurationGenerator = item =>
                    recorder.Record(item, TimeSpan.FromMinutes(1));
            })
            .WithName("typed-breaker");

        _ = await shield.ExecuteAsync(static _ => new ValueTask<int>(-1));

        var record = recorder.Callbacks.Single();
        await Assert.That(record.Kind).IsEqualTo(CallbackKind.CircuitBreakDuration);
        await Assert.That(record.ShieldName).IsEqualTo("typed-breaker");
        await Assert.That(record.StrategyIndex).IsEqualTo(0);
        await Assert.That(record.Result).IsEqualTo(-1);
        await Assert.That(record.FailureRate).IsEqualTo(1);
        await Assert.That(record.FailureCount).IsEqualTo(1);
        await Assert.That(record.ConsecutiveFailures).IsEqualTo(1);
    }

    [Test]
    public async Task Records_Callback_Errors_From_Diagnostics()
    {
        using var recorder = new TelemetryRecorder(
            captureMetrics: false,
            captureCallbackErrors: true);
        var callbackFailure = new IOException("callback");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => throw callbackFailure;
        }).WithName("recorded-callback-error");

        _ = await shield.ExecuteOutcomeAsync<int>(static _ => throw new InvalidOperationException("operation"));

        var record = recorder.Callbacks.Single();
        await Assert.That(record.Kind).IsEqualTo(CallbackKind.CallbackError);
        await Assert.That(record.ErrorKind).IsEqualTo(CallbackErrorKind.Retry);
        await Assert.That(record.ShieldName).IsEqualTo("recorded-callback-error");
        await Assert.That(record.StrategyIndex).IsEqualTo(0);
        await Assert.That(ReferenceEquals(record.Exception, callbackFailure)).IsTrue();
    }

    [Test]
    public async Task Callback_Error_Capture_Is_Opt_In()
    {
        using var unrelated = new TelemetryRecorder(captureMetrics: false);
        using var capturing = new TelemetryRecorder(
            captureMetrics: false,
            captureCallbackErrors: true);
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = static _ => throw new IOException("callback");
        });

        _ = await shield.ExecuteOutcomeAsync<int>(static _ =>
            throw new InvalidOperationException("operation"));

        await Assert.That(unrelated.Callbacks).IsEmpty();
        await Assert.That(capturing.Callbacks.Single().Kind)
            .IsEqualTo(CallbackKind.CallbackError);
    }

    [Test]
    public async Task Waiters_Honor_Cancellation()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.That(async () =>
                await recorder.WaitForCallbackCountAsync(1, cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Disposal_Releases_Pending_Waiters_With_ObjectDisposedException()
    {
        var recorder = new TelemetryRecorder(captureMetrics: false);
        var callbackWait = recorder.WaitForCallbackCountAsync(1);
        var metricWait = recorder.WaitForMetricCountAsync(1);

        recorder.Dispose();

        await Assert.That(async () => await callbackWait)
            .Throws<ObjectDisposedException>();
        await Assert.That(async () => await metricWait)
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Disposed_Recorder_Callback_Does_Not_Replace_The_Outcome()
    {
        var recorder = new TelemetryRecorder(captureMetrics: false);
        recorder.Dispose();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry =>
            {
                recorder.Record(retry);
                return default;
            };
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(
            static _ => throw new InvalidOperationException());

        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(() => recorder.WaitForCallbackCountAsync(0))
            .Throws<ObjectDisposedException>();
        await Assert.That(() => recorder.WaitForMetricCountAsync(0))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Waiters_Reject_Negative_Counts()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);

        await Assert.That(() => recorder.WaitForCallbackCountAsync(-1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => recorder.WaitForMetricCountAsync(-1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Records_Every_Documented_Metric_Family()
    {
        using var recorder = new TelemetryRecorder();
        var name = $"metric-families-{Guid.NewGuid():N}";

        var retryAttempts = 0;
        await Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => throw new IOException("telemetry callback");
        }).WithName(name).ExecuteAsync(_ =>
            Interlocked.Increment(ref retryAttempts) == 1
                ? ValueTask.FromException(new InvalidOperationException())
                : ValueTask.CompletedTask);

        await Shield.Timeout(TimeSpan.FromMilliseconds(1)).WithName(name)
            .ExecuteOutcomeAsync<int>(static async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            });

        await Shield.Hedge(1, TimeSpan.Zero).WithName(name).ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException()));
        await Shield.For<int>().When<InvalidOperationException>().FallbackTo(42).WithName(name)
            .ExecuteAsync<int>(static _ => throw new InvalidOperationException());

        var breaker = Shield.CircuitBreaker(1, TimeSpan.FromMinutes(1)).WithName(name);
        await breaker.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException()));
        await breaker.ExecuteOutcomeAsync<int>(static _ => new ValueTask<int>(42));

        await Shield.ConcurrencyLimit(1).WithName(name)
            .ExecuteAsync(static _ => ValueTask.CompletedTask);
        await Shield.RateLimit(1, TimeSpan.FromMinutes(1)).WithName(name)
            .ExecuteAsync(static _ => ValueTask.CompletedTask);

        var namedInstruments = recorder.Metrics
            .Where(record => record.Tags.TryGetValue("kevlar.shield.name", out var value)
                && Equals(value, name))
            .Select(record => record.InstrumentName)
            .ToHashSet(StringComparer.Ordinal);
        var expectedNamed = new[]
        {
            "kevlar.executions",
            "kevlar.execution.duration",
            "kevlar.retries",
            "kevlar.timeouts",
            "kevlar.hedges",
            "kevlar.hedge_attempts",
            "kevlar.fallbacks",
            "kevlar.rejections",
            "kevlar.callback_errors",
            "kevlar.strategy.events",
            "kevlar.attempt.duration",
#if NET9_0_OR_GREATER
            "kevlar.circuit_breaker.state",
            "kevlar.concurrency_limit.inflight",
            "kevlar.concurrency_limit.queued",
            "kevlar.concurrency_limit.capacity",
            "kevlar.rate_limit.available",
            "kevlar.rate_limit.queued",
#endif
        };

        await Assert.That(expectedNamed.All(namedInstruments.Contains)).IsTrue();
        await Assert.That(recorder.Metrics.Any(record =>
            record.InstrumentName == "kevlar.circuit_breaker.transitions")).IsTrue();
    }

    private static async ValueTask<int> WaitForCancellationAsync(
        CancellationToken cancellationToken,
        TaskCompletionSource started)
    {
        started.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}

namespace Kevlar.Testing.Tests;

[NotInParallel]
public class TelemetryRecorderTests
{
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

    [Test]
    public async Task Records_Typed_And_Untyped_Callbacks_In_Order()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        var typed = Shield.For<int>()
            .WhenResult(static result => result < 0)
            .FallbackTo(42, options => options.OnFallback = recorder.Record)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = recorder.Record;
            })
            .WithName("typed");
        var attempts = 0;

        var result = await typed.ExecuteAsync(_ => new ValueTask<int>(
            Interlocked.Increment(ref attempts) == 1 ? -1 : -2));

        await Assert.That(result).IsEqualTo(42);
        var records = recorder.Callbacks;
        await Assert.That(records.Select(record => record.Kind)
            .SequenceEqual([CallbackKind.Retry, CallbackKind.Fallback])).IsTrue();
        await Assert.That(records[0].RetryNumber).IsEqualTo(1);
        await Assert.That(records[0].AttemptNumber).IsNull();
        await Assert.That(records[0].Result).IsEqualTo(-1);
        await Assert.That(records[0].ShieldName).IsEqualTo("typed");
        await Assert.That(records[1].Result).IsEqualTo(-2);

        var untyped = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = recorder.Record;
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
                options.OnRetry = recorder.Record;
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
            options.OnTimeout = recorder.Record;
        }).WithName("timeout");
        await timeout.ExecuteOutcomeAsync<int>(static async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        });

        var hedge = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = recorder.Record;
        }).WithName("hedge");
        await hedge.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException()));

        var breaker = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.OnStateChanged = recorder.Record;
        });
        await breaker.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new ApplicationException("break")));

        var records = recorder.Callbacks;
        await Assert.That(records.Select(record => record.Kind).SequenceEqual(
            [CallbackKind.Timeout, CallbackKind.Hedge, CallbackKind.CircuitTransition])).IsTrue();
        await Assert.That(records[0].ShieldName).IsEqualTo("timeout");
        await Assert.That(records[0].Timeout).IsEqualTo(TimeSpan.FromMilliseconds(1));
        await Assert.That(records[1].ShieldName).IsEqualTo("hedge");
        await Assert.That(records[1].AttemptNumber).IsEqualTo(2);
        await Assert.That(records[1].RetryNumber).IsNull();
        await Assert.That(records[2].From).IsEqualTo(CircuitState.Closed);
        await Assert.That(records[2].To).IsEqualTo(CircuitState.Open);
        await Assert.That(records[2].Exception).IsTypeOf<ApplicationException>();
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
    public async Task Disposed_Recorder_Rejects_Callbacks_And_New_Waiters()
    {
        var recorder = new TelemetryRecorder(captureMetrics: false);
        recorder.Dispose();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = recorder.Record;
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(
            static _ => throw new InvalidOperationException());

        await Assert.That(outcome.Exception).IsTypeOf<ObjectDisposedException>();
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
        await Shield.Retry(1, Backoff.None).WithName(name).ExecuteAsync(_ =>
            Interlocked.Increment(ref retryAttempts) == 1
                ? ValueTask.FromException(new InvalidOperationException())
                : ValueTask.CompletedTask);

        await Shield.Timeout(TimeSpan.FromMilliseconds(1)).WithName(name)
            .ExecuteOutcomeAsync<int>(static async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            });

        await Shield.Hedge(2, TimeSpan.Zero).WithName(name).ExecuteOutcomeAsync<int>(static _ =>
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
            "kevlar.fallbacks",
            "kevlar.rejections",
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
}

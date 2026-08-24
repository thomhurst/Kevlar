using System.Diagnostics.Metrics;
using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Testing.Tests;

public class TimeControlTests
{
    [Test]
    public async Task Retry_Advances_Until_All_Delays_Complete()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var shield = Shield
            .Retry(2, Backoff.Constant(TimeSpan.FromSeconds(1)))
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync<int>(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            return attempt < 3
                ? ValueTask.FromException<int>(new InvalidOperationException($"attempt {attempt}"))
                : new ValueTask<int>(42);
        }).AsTask();

        await execution.WaitForPendingAsync(
            () => Volatile.Read(ref attempts) == 1,
            "the first retry delay");
        await timeProvider.AdvanceUntilAsync(
            TimeSpan.FromSeconds(1),
            () => Volatile.Read(ref attempts) == 3,
            "all retry attempts",
            maxAdvances: 2);

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Timeout_Advances_To_The_Deadline()
    {
        var timeProvider = new FakeTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(5);
        var shield = Shield.Timeout(TimeSpan.FromSeconds(5)).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync<int>(async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            return 0;
        }).AsTask();

        await execution.WaitForPendingAsync(
            () => started.Task.IsCompleted,
            "the timed execution");
        await timeProvider.AdvanceUntilAsync(
            TimeSpan.FromSeconds(5),
            () => timeProvider.GetUtcNow() >= deadline,
            "the timeout deadline",
            maxAdvances: 1);

        _ = await Assert.That(async () => await execution).Throws<TimeoutExceededException>();
    }

    [Test]
    public async Task Circuit_Breaker_Window_Advances_Without_Real_Time()
    {
        var timeProvider = new FakeTimeProvider();
        var shield = Shield
            .CircuitBreaker(1, TimeSpan.FromSeconds(10))
            .WithTimeProvider(timeProvider);

        _ = await Assert.That(() => shield.Execute<int>(
                static _ => throw new InvalidOperationException("open")))
            .Throws<InvalidOperationException>();
        _ = await Assert.That(() => shield.Execute(static _ => 1))
            .Throws<CircuitOpenException>();

        var closesAt = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(10);
        await timeProvider.AdvanceUntilAsync(
            TimeSpan.FromSeconds(2),
            () => timeProvider.GetUtcNow() >= closesAt,
            "the circuit-breaker window",
            maxAdvances: 5);

        await Assert.That(shield.Execute(static _ => 42)).IsEqualTo(42);
    }

#if NET9_0_OR_GREATER
    [Test]
    public async Task Rate_Limit_Replenishment_Completes_Queued_Execution()
    {
        const string ShieldName = "testing-rate-queue";
        using var admission = new QueueAdmissionSignal("kevlar.rate_limit.queued", ShieldName);
        var timeProvider = new FakeTimeProvider();
        var executions = 0;
        var shield = Shield
            .RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(1);
                options.QueueLimit = 1;
            })
            .WithTimeProvider(timeProvider)
            .WithName(ShieldName);

        await Assert.That(await shield.ExecuteAsync(_ =>
        {
            Interlocked.Increment(ref executions);
            return new ValueTask<int>(1);
        })).IsEqualTo(1);
        var queued = shield.ExecuteAsync(_ =>
        {
            Interlocked.Increment(ref executions);
            return new ValueTask<int>(2);
        }).AsTask();

        await queued.WaitForPendingAsync(
            () => admission.WasObserved,
            "the rate-limit queue");
        await timeProvider.AdvanceUntilAsync(
            TimeSpan.FromSeconds(1),
            () => Volatile.Read(ref executions) == 2,
            "the queued rate-limit execution",
            maxAdvances: 1);

        await Assert.That(await queued).IsEqualTo(2);
    }
#endif

#if NET9_0_OR_GREATER
    [Test]
    public async Task Concurrency_Queue_Completes_After_Permit_Release()
    {
        const string ShieldName = "testing-concurrency-queue";
        using var admission = new QueueAdmissionSignal("kevlar.concurrency_limit.queued", ShieldName);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.ConcurrencyLimit(1, maxQueue: 1).WithName(ShieldName);
        var first = shield.ExecuteAsync<int>(async _ =>
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return 1;
        }).AsTask();
        var queued = shield.ExecuteAsync(static _ => new ValueTask<int>(2)).AsTask();

        await queued.WaitForPendingAsync(
            () => admission.WasObserved,
            "the concurrency-limit queue");
        release.TrySetResult();
        await Assert.That(await first).IsEqualTo(1);

        await Assert.That(await queued).IsEqualTo(2);
    }
#endif

    [Test]
    public async Task Hedging_Stagger_Advances_To_The_Next_Attempt()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var shield = Shield
            .Hedge(2, TimeSpan.FromSeconds(1))
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync<int>(async token =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return 42;
        }).AsTask();

        await execution.WaitForPendingAsync(
            () => Volatile.Read(ref attempts) == 1,
            "the primary hedge attempt");
        await timeProvider.AdvanceUntilAsync(
            TimeSpan.FromSeconds(1),
            () => Volatile.Read(ref attempts) == 2,
            "the hedged attempt",
            maxAdvances: 1);

        await Assert.That(await execution).IsEqualTo(42);
    }

    [Test]
    public async Task Cancellation_Completes_A_Pending_Execution()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = Shield.Empty.ExecuteAsync<int>(async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        }, cancellation.Token).AsTask();

        await execution.WaitForPendingAsync(
            () => started.Task.IsCompleted,
            "the cancellable execution");
        cancellation.Cancel();

        _ = await Assert.That(async () => await execution).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Pending_Wait_Preserves_Cancellation_After_Final_Yield()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var wait = pending.Task.WaitForPendingAsync(
            () =>
            {
                cancellation.Cancel();
                return false;
            },
            "cancelled work",
            maxYields: 1,
            cancellationToken: cancellation.Token);
        var exception = await Assert.That(async () => await wait)
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Bounds_Report_Execution_And_Condition_Details()
    {
        var completed = Task.CompletedTask;
        var pendingFailure = await Assert.That(async () => await completed.WaitForPendingAsync(
                static () => false,
                "a retry delay",
                maxYields: 1))
            .Throws<ShieldAssertionException>();
        await Assert.That(pendingFailure!.Message).Contains("a retry delay");
        await Assert.That(pendingFailure.Message).Contains("RanToCompletion");

        var neverStarts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var missingWork = await Assert.That(async () => await neverStarts.Task.WaitForPendingAsync(
                static () => false,
                "a missing attempt",
                maxYields: 1))
            .Throws<ShieldAssertionException>();
        await Assert.That(missingWork!.Message).Contains("1 scheduler yield");
        await Assert.That(missingWork.Message).Contains("WaitingForActivation");

        var timeProvider = new FakeTimeProvider();
        var advanceFailure = await Assert.That(async () => await timeProvider.AdvanceUntilAsync(
                TimeSpan.FromSeconds(1),
                static () => false,
                "an unreachable state",
                maxAdvances: 2,
                maxYieldsPerAdvance: 1))
            .Throws<ShieldAssertionException>();
        await Assert.That(advanceFailure!.Message).Contains("an unreachable state");
        await Assert.That(advanceFailure.Message).Contains("2 advances");
        await Assert.That(advanceFailure.Message).Contains("00:00:01");
    }

    [Test]
    public async Task Invalid_Bounds_Are_Rejected()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = await Assert.That(
                async () => await pending.Task.WaitForPendingAsync(
                    static () => false,
                    "pending work",
                    maxYields: 0))
            .Throws<ArgumentOutOfRangeException>();

        var timeProvider = new FakeTimeProvider();
        _ = await Assert.That(async () => await timeProvider.AdvanceUntilAsync(
                TimeSpan.Zero,
                static () => false,
                "work",
                maxAdvances: 1))
            .Throws<ArgumentOutOfRangeException>();
        _ = await Assert.That(async () => await timeProvider.AdvanceUntilAsync(
                TimeSpan.FromSeconds(1),
                static () => false,
                "work",
                maxAdvances: 0))
            .Throws<ArgumentOutOfRangeException>();
        _ = await Assert.That(async () => await timeProvider.AdvanceUntilAsync(
                TimeSpan.FromSeconds(1),
                static () => false,
                "work",
                maxYieldsPerAdvance: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    private sealed class QueueAdmissionSignal : IDisposable
    {
        private readonly string _instrumentName;
        private readonly MeterListener _listener = new();
        private readonly string _shieldName;
        private int _wasObserved;

        public QueueAdmissionSignal(string instrumentName, string shieldName)
        {
            _instrumentName = instrumentName;
            _shieldName = shieldName;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Kevlar" && instrument.Name == _instrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(ObserveMeasurement);
            _listener.Start();
        }

        public bool WasObserved => Volatile.Read(ref _wasObserved) != 0;

        public void Dispose() => _listener.Dispose();

        private void ObserveMeasurement(
            Instrument instrument,
            long value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            if (value != 1)
            {
                return;
            }

            foreach (var tag in tags)
            {
                if (tag.Key == "kevlar.shield.name" && Equals(tag.Value, _shieldName))
                {
                    Volatile.Write(ref _wasObserved, 1);
                    return;
                }
            }
        }
    }
}

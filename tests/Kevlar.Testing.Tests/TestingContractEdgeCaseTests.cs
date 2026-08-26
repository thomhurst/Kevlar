using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Testing.Tests;

public class TestingContractEdgeCaseTests
{
    [Test]
    public async Task Descriptor_Extensions_Reject_Null_For_Untyped_And_Typed_Shields()
    {
        Shield? untyped = null;
        Shield<int>? typed = null;

        await Assert.That(() => untyped!.GetDescriptor())
            .Throws<ArgumentNullException>();
        await Assert.That(() => typed!.GetDescriptor())
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Descriptor_Assertions_Reject_Null_And_Invalid_Arguments()
    {
        ShieldDescriptor? missing = null;
        var descriptor = Shield.Empty.GetDescriptor();

        await Assert.That(() => missing!.AssertContains<RetryStrategyDescriptor>())
            .Throws<ArgumentNullException>();
        await Assert.That(() => missing!.AssertStrategyOrder())
            .Throws<ArgumentNullException>();
        await Assert.That(() => descriptor.AssertStrategyOrder((StrategyKind[])null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => missing!.AssertStrategyCount(0))
            .Throws<ArgumentNullException>();
        await Assert.That(() => descriptor.AssertStrategyCount(-1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => missing!.AssertContainsSingle<RetryStrategyDescriptor>())
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Descriptor_Assertions_Report_Missing_And_Duplicate_Strategies()
    {
        var empty = Shield.Empty.GetDescriptor();
        var duplicate = Shield.Retry(1).Retry(2).GetDescriptor();

        var missing = await Assert.That(() => empty.AssertContains<RetryStrategyDescriptor>())
            .Throws<ShieldAssertionException>();
        var duplicates = await Assert.That(() => duplicate.AssertContainsSingle<RetryStrategyDescriptor>())
            .Throws<ShieldAssertionException>();

        await Assert.That(missing!.Message).Contains("actual 0");
        await Assert.That(missing.Message).Contains("Pipeline: []");
        await Assert.That(duplicates!.Message).Contains("actual 2");
        await Assert.That(duplicates.Message).Contains("Retry, Retry");
    }

    [Test]
    public async Task Pending_Wait_Rejects_Every_Invalid_Argument()
    {
        Task? missingExecution = null;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        await Assert.That(async () => await missingExecution!.WaitForPendingAsync(
                static () => true,
                "missing execution"))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await pending.WaitForPendingAsync(
                null!,
                "missing predicate"))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await pending.WaitForPendingAsync(
                static () => true,
                " "))
            .Throws<ArgumentException>();
        await Assert.That(async () => await pending.WaitForPendingAsync(
                static () => true,
                "invalid yield bound",
                maxYields: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Typed_ExecutionProbe_Tracks_Active_Cancellation()
    {
        var probe = new ExecutionProbe();
        using var cancellation = new CancellationTokenSource();
        var execution = probe.Wrap<int>(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            return 42;
        })(cancellation.Token).AsTask();

        await probe.WaitForAttemptCountAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.That(async () => await execution)
            .Throws<OperationCanceledException>();
        await probe.WaitForCancellationCountAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(probe.GetSnapshot().CancellationCount).IsEqualTo(1);
    }

    [Test]
    public async Task Probe_Wait_With_Live_Cancellation_Completes_On_Count_Change()
    {
        var probe = new ExecutionProbe();
        using var cancellation = new CancellationTokenSource();
        var wait = probe.WaitForAttemptCountAsync(1, cancellation.Token);

        await probe.Wrap(static _ => ValueTask.CompletedTask)(CancellationToken.None);

        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(wait.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task FakeTime_Checks_Condition_After_Final_Scheduler_Drain()
    {
        var timeProvider = new FakeTimeProvider();
        var initial = timeProvider.GetUtcNow();
        var evaluations = 0;

        await timeProvider.AdvanceUntilAsync(
            TimeSpan.FromSeconds(1),
            () => Interlocked.Increment(ref evaluations) == 4,
            "condition at final boundary",
            maxAdvances: 1,
            maxYieldsPerAdvance: 1);

        await Assert.That(evaluations).IsEqualTo(4);
        await Assert.That(timeProvider.GetUtcNow())
            .IsEqualTo(initial.AddSeconds(1));
    }

    [Test]
    public async Task Recorder_Captures_Untyped_Fallback_And_Completes_Cancellable_Waiter()
    {
        using var recorder = new TelemetryRecorder(captureMetrics: false);
        using var cancellation = new CancellationTokenSource();
        var wait = recorder.WaitForCallbackCountAsync(1, cancellation.Token);
        var shield = Shield.When<InvalidOperationException>().Fallback(
            static (_, _) => ValueTask.CompletedTask,
            options => options.OnFallback = fallback =>
            {
                recorder.Record(fallback);
                return default;
            });

        await shield.ExecuteAsync(static _ =>
            ValueTask.FromException(new InvalidOperationException("handled")));
        await wait.WaitAsync(TimeSpan.FromSeconds(5));

        var callback = recorder.Callbacks.Single();
        await Assert.That(callback.Kind).IsEqualTo(CallbackKind.Fallback);
        await Assert.That(callback.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(callback.Result).IsNull();
    }
}

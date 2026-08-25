using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Testing.Tests;

public class HelperContractTests
{
    [Test]
    public async Task ExecutionProbe_Rejects_Invalid_Inputs_Without_Changing_Counts()
    {
        var probe = new ExecutionProbe();

        await Assert.That(() => probe.Wrap((Func<CancellationToken, ValueTask>)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => probe.Wrap<int>((Func<CancellationToken, ValueTask<int>>)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => probe.WaitForAttemptCountAsync(-1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => probe.WaitForCancellationCountAsync(-1))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(probe.AttemptCount).IsEqualTo(0);
        await Assert.That(probe.CancellationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ExecutionProbe_Only_Counts_Cancellation_While_An_Attempt_Is_Active()
    {
        var probe = new ExecutionProbe();
        using var cancellation = new CancellationTokenSource();

        await Shield.Empty.ExecuteAsync(
            probe.Wrap(static _ => ValueTask.CompletedTask),
            cancellation.Token);
        cancellation.Cancel();

        await Assert.That(probe.AttemptCount).IsEqualTo(1);
        await Assert.That(probe.CancellationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ExecutionProbe_Releases_All_Concurrent_Waiters()
    {
        var probe = new ExecutionProbe();
        var waiters = Enumerable.Range(0, 16)
            .Select(_ => probe.WaitForAttemptCountAsync(1))
            .ToArray();

        await Shield.Empty.ExecuteAsync(probe.Wrap(static _ => ValueTask.CompletedTask));
        await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(waiters).All(static waiter => waiter.IsCompletedSuccessfully);
    }

    [Test]
    public async Task StateSnapshot_Supports_Fallback_Shields_And_Preserves_Pipeline_Indexes()
    {
        var shield = Shield
            .Fallback(static _ => ValueTask.CompletedTask)
            .Timeout(TimeSpan.FromSeconds(1))
            .ConcurrencyLimit(3)
            .Retry(1, Backoff.None);

        var snapshot = shield.GetStateSnapshot();
        var concurrency = snapshot.Strategies.Single();

        await Assert.That(concurrency).IsTypeOf<ConcurrencyLimitStateSnapshot>();
        await Assert.That(concurrency.Kind).IsEqualTo(StrategyKind.ConcurrencyLimit);
        await Assert.That(concurrency.StrategyIndex).IsEqualTo(2);
    }

    [Test]
    public async Task FakeTime_Does_Not_Advance_When_Condition_Is_Already_Satisfied()
    {
        var timeProvider = new FakeTimeProvider();
        var initial = timeProvider.GetUtcNow();
        var evaluations = 0;

        await timeProvider.AdvanceUntilAsync(
            TimeSpan.FromHours(1),
            () => Interlocked.Increment(ref evaluations) == 1,
            "an already-satisfied condition");

        await Assert.That(timeProvider.GetUtcNow()).IsEqualTo(initial);
        await Assert.That(evaluations).IsEqualTo(1);
    }

    [Test]
    public async Task FakeTime_Advancement_Preserves_Cancellation_Token()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.That(async () => await timeProvider.AdvanceUntilAsync(
                TimeSpan.FromSeconds(1),
                static () => false,
                "cancelled advancement",
                cancellationToken: cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }
}

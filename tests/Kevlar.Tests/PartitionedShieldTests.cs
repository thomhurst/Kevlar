using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class PartitionedShieldTests
{
    [Test]
    public async Task Concurrent_First_Lookup_Creates_One_Partition()
    {
        var creations = 0;
        var provider = new PartitionedShield<string>(_ =>
        {
            Interlocked.Increment(ref creations);
            Thread.Sleep(10);
            return Shield.Retry(1, Backoff.None);
        });

        var lookups = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => provider.GetShield("tenant")))
            .ToArray();
        var shields = await Task.WhenAll(lookups);

        await Assert.That(creations).IsEqualTo(1);
        await Assert.That(provider.CreatedCount).IsEqualTo(1);
        await Assert.That(provider.Count).IsEqualTo(1);
        await Assert.That(shields.All(shield => ReferenceEquals(shield, shields[0]))).IsTrue();
    }

    [Test]
    public async Task Factory_Can_Wait_For_An_Unrelated_Provider_Lookup()
    {
        PartitionedShield<string>? provider = null;
        provider = new PartitionedShield<string>(key =>
        {
            if (key == "dependent")
            {
                var lookup = Task.Run(() => provider!.GetShield("warm"));
                if (!lookup.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The unrelated partition lookup was blocked by the factory.");
                }
            }

            return Shield.Empty;
        });
        _ = provider.GetShield("warm");

        var dependent = await Task.Run(() => provider.GetShield("dependent"))
            .WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(ReferenceEquals(dependent, Shield.Empty)).IsTrue();
        await Assert.That(provider.CreatedCount).IsEqualTo(2);
    }

    [Test]
    public async Task Capacity_Uses_Lru_And_Recreates_Fresh_State()
    {
        var provider = new PartitionedShield<string>(
            _ => Shield.CircuitBreaker(1, TimeSpan.FromHours(1)),
            new PartitionedShieldOptions { MaximumPartitions = 2 });
        var firstA = provider.GetShield("a");
        var firstB = provider.GetShield("b");
        _ = provider.GetShield("a");

        _ = provider.GetShield("c");

        await Assert.That(provider.TryGetShield("b", out _)).IsFalse();
        await Assert.That(provider.TryGetShield("a", out var retainedA)).IsTrue();
        await Assert.That(ReferenceEquals(retainedA, firstA)).IsTrue();
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(1);

        var oldResult = await firstB.ExecuteAsync(_ => new ValueTask<int>(42));
        var recreatedB = provider.GetShield("b");
        await Assert.That(oldResult).IsEqualTo(42);
        await Assert.That(ReferenceEquals(recreatedB, firstB)).IsFalse();
        await Assert.That(provider.CreatedCount).IsEqualTo(4);
    }

    [Test]
    public async Task Idle_Expiry_Is_Explicit_And_Opportunistic()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = new PartitionedShield<string>(
            _ => Shield.Retry(0, Backoff.None),
            new PartitionedShieldOptions
            {
                MaximumPartitions = 10,
                IdleExpiration = TimeSpan.FromMinutes(1),
                TimeProvider = timeProvider,
            });
        var first = provider.GetShield("tenant");

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var removed = provider.PruneExpired();
        var second = provider.GetShield("tenant");

        await Assert.That(removed).IsEqualTo(1);
        await Assert.That(provider.ExpirationEvictionCount).IsEqualTo(1);
        await Assert.That(provider.CreatedCount).IsEqualTo(2);
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }

    [Test]
    public async Task Circuit_State_Is_Shared_Within_A_Key_And_Isolated_Between_Keys()
    {
        var provider = new PartitionedShield<string>(_ =>
            Shield.When<InvalidOperationException>()
                .CircuitBreaker(1, TimeSpan.FromHours(1)));
        var calls = 0;

        _ = await Assert.That(async () =>
                await provider.GetShield("a").ExecuteAsync<int>(_ =>
                {
                    calls++;
                    throw new InvalidOperationException();
                }))
            .Throws<InvalidOperationException>();
        _ = await Assert.That(async () =>
                await provider.GetShield("a").ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
        var other = await provider.GetShield("b").ExecuteAsync(_ =>
        {
            calls++;
            return new ValueTask<int>(2);
        });

        await Assert.That(other).IsEqualTo(2);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_Partitions_Preserve_Result_Aware_State()
    {
        var provider = new PartitionedShield<string, int>(_ =>
            Shield.For<int>().WhenResult(-1).Fallback(0));

        var result = await provider.GetShield("typed")
            .ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(result).IsEqualTo(0);
        await Assert.That(provider.TryGetShield("typed", out var retained)).IsTrue();
        await Assert.That(ReferenceEquals(retained, provider.GetShield("typed"))).IsTrue();
    }

    [Test]
    public async Task Void_Partitions_Preserve_The_Void_Only_Type_And_State()
    {
        var fallbackCalls = 0;
        var provider = new PartitionedVoidShield<string>(_ =>
            Shield.When<InvalidOperationException>().Fallback((_, _) =>
            {
                fallbackCalls++;
                return ValueTask.CompletedTask;
            }));

        await provider.GetShield("void").ExecuteAsync(static _ =>
            ValueTask.FromException(new InvalidOperationException()));

        await Assert.That(fallbackCalls).IsEqualTo(1);
        await Assert.That(provider.TryGetShield("void", out var retained)).IsTrue();
        await Assert.That(ReferenceEquals(retained, provider.GetShield("void"))).IsTrue();
        await Assert.That(provider.CreatedCount).IsEqualTo(1);
    }

    [Test]
    public async Task Evicting_An_Active_Partition_Does_Not_Cancel_Its_Execution()
    {
        var provider = new PartitionedShield<string>(
            _ => Shield.Retry(0, Backoff.None),
            new PartitionedShieldOptions { MaximumPartitions = 1 });
        var first = provider.GetShield("first");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var execution = first.ExecuteOutcomeAsync<int>(async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();
        await entered.Task;

        _ = provider.GetShield("second");
        cancellation.Cancel();
        var outcome = await execution;
        var recreated = provider.GetShield("first");

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(ReferenceEquals(first, recreated)).IsFalse();
    }

    [Test]
    public async Task Factory_Failure_Does_Not_Evict_A_Retained_Partition()
    {
        var provider = new PartitionedShield<string>(
            key => key == "bad" ? throw new InvalidOperationException("factory") : Shield.Empty,
            new PartitionedShieldOptions { MaximumPartitions = 1 });
        var retained = provider.GetShield("good");

        _ = await Assert.That(() => provider.GetShield("bad")).Throws<InvalidOperationException>();

        await Assert.That(provider.TryGetShield("good", out var actual)).IsTrue();
        await Assert.That(ReferenceEquals(actual, retained)).IsTrue();
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(0);
    }

    [Test]
    public async Task Remove_And_Clear_Do_Not_Count_As_Automatic_Evictions()
    {
        var provider = new PartitionedShield<int>(_ => Shield.Empty);
        _ = provider.GetShield(1);
        _ = provider.GetShield(2);

        await Assert.That(provider.Remove(1)).IsTrue();
        await Assert.That(provider.Remove(1)).IsFalse();
        provider.Clear();

        await Assert.That(provider.Count).IsEqualTo(0);
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(0);
        await Assert.That(provider.ExpirationEvictionCount).IsEqualTo(0);
    }

    [Test]
    public async Task Invalid_Options_And_Keys_Are_Rejected()
    {
        _ = await Assert.That(() => new PartitionedShield<string>(
                _ => Shield.Empty,
                new PartitionedShieldOptions { MaximumPartitions = 0 }))
            .Throws<ArgumentOutOfRangeException>();
        _ = await Assert.That(() => new PartitionedShield<string>(
                _ => Shield.Empty,
                new PartitionedShieldOptions { IdleExpiration = TimeSpan.Zero }))
            .Throws<ArgumentOutOfRangeException>();
        _ = await Assert.That(() => new PartitionedShield<string>(null!))
            .Throws<ArgumentNullException>();

        var provider = new PartitionedShield<string>(_ => Shield.Empty);
        _ = await Assert.That(() => provider.GetShield(null!)).Throws<ArgumentNullException>();
    }
}

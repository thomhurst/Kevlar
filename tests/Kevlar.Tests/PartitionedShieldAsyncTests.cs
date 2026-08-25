using System.Collections.Concurrent;
using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class PartitionedShieldAsyncTests
{
    [Test]
    public async Task Concurrent_First_Callers_Do_Not_Block_ThreadPool_Threads()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creations = 0;
        var provider = PartitionedShield<string>.CreateAsync(async _ =>
        {
            Interlocked.Increment(ref creations);
            entered.TrySetResult();
            await release.Task;
            return Shield.Empty;
        });

        var lookups = Enumerable.Range(0, 64)
            .Select(_ => provider.GetShieldAsync("tenant").AsTask())
            .ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(lookups.All(lookup => !lookup.IsCompleted)).IsTrue();
        release.TrySetResult();
        var shields = await Task.WhenAll(lookups).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(creations).IsEqualTo(1);
        await Assert.That(shields.All(shield => ReferenceEquals(shield, Shield.Empty))).IsTrue();
    }

    [Test]
    public async Task Sync_First_Callers_Block_Until_Factory_Completes()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var provider = new PartitionedShield<string>(_ =>
        {
            entered.Set();
            release.Wait();
            return Shield.Empty;
        });

        var first = Task.Run(() => provider.GetShield("tenant"));
        await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5)));
        var second = Task.Run(() => provider.GetShield("tenant"));
        await Task.Delay(50);

        await Assert.That(second.IsCompleted).IsFalse();
        release.Set();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Async_Factory_Exception_Is_Not_Cached()
    {
        var attempts = 0;
        var provider = PartitionedShield<string>.CreateAsync(_ =>
            Interlocked.Increment(ref attempts) == 1
                ? ValueTask.FromException<Shield>(new InvalidOperationException("factory"))
                : new ValueTask<Shield>(Shield.Empty));

        _ = await Assert.That(async () => await provider.GetShieldAsync("tenant"))
            .Throws<InvalidOperationException>();
        var shield = await provider.GetShieldAsync("tenant");

        await Assert.That(shield).IsSameReferenceAs(Shield.Empty);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Factory_Runs_Exactly_Once_Per_Key_Under_Contention()
    {
        var creations = new ConcurrentDictionary<int, int>();
        var provider = PartitionedShield<int>.CreateAsync(async key =>
        {
            creations.AddOrUpdate(key, 1, static (_, count) => count + 1);
            await Task.Yield();
            return Shield.Empty;
        });

        var lookups = Enumerable.Range(0, 32)
            .SelectMany(_ => Enumerable.Range(0, 100))
            .Select(key => provider.GetShieldAsync(key).AsTask())
            .ToArray();
        await Task.WhenAll(lookups).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(provider.Count).IsEqualTo(100);
        await Assert.That(creations.Count).IsEqualTo(100);
        await Assert.That(creations.Values.All(count => count == 1)).IsTrue();
    }

    [Test]
    public async Task Eviction_Callbacks_Report_Capacity_And_Idle_Reasons()
    {
        var timeProvider = new FakeTimeProvider();
        var evictions = new List<(string Key, PartitionEvictionReason Reason)>();
        var provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions
            {
                MaximumPartitions = 2,
                IdleExpiration = TimeSpan.FromMinutes(1),
                TimeProvider = timeProvider,
                OnEvicted = item => evictions.Add(((string)item.Key, item.Reason)),
            });
        _ = provider.GetShield("first");
        _ = provider.GetShield("second");

        _ = provider.GetShield("third");
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        _ = await provider.PruneExpiredAsync();

        await Assert.That(evictions).IsEquivalentTo([
            ("first", PartitionEvictionReason.Capacity),
            ("second", PartitionEvictionReason.Idle),
            ("third", PartitionEvictionReason.Idle),
        ]);
    }

    [Test]
    public async Task Async_Eviction_Callback_Completes_Before_Slot_Reuse()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions
            {
                MaximumPartitions = 1,
                OnEvictedAsync = async _ =>
                {
                    entered.TrySetResult();
                    await release.Task;
                },
            });
        _ = provider.GetShield("first");

        var second = provider.GetShieldAsync("second").AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(second.IsCompleted).IsFalse();
        await Assert.That(provider.Count).IsEqualTo(0);
        release.TrySetResult();
        _ = await second.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(provider.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Throwing_Eviction_Callback_Does_Not_Fail_Lookup()
    {
        var provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions
            {
                MaximumPartitions = 1,
                OnEvicted = static _ => throw new InvalidOperationException("observer"),
                OnEvictedAsync = static _ => ValueTask.FromException(new InvalidOperationException("observer")),
            });
        _ = provider.GetShield("first");

        var second = await provider.GetShieldAsync("second");

        await Assert.That(second).IsSameReferenceAs(Shield.Empty);
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(1);
    }

    [Test]
    public async Task Evicted_Resources_Can_Be_Disposed_By_Key()
    {
        var resources = new ConcurrentDictionary<string, DisposableResource>();
        var provider = new PartitionedShield<string>(
            key =>
            {
                resources[key] = new DisposableResource();
                return Shield.Empty;
            },
            new PartitionedShieldOptions
            {
                MaximumPartitions = 1,
                OnEvicted = item => resources[(string)item.Key].Dispose(),
            });
        _ = provider.GetShield("first");

        _ = provider.GetShield("second");

        await Assert.That(resources["first"].IsDisposed).IsTrue();
        await Assert.That(resources["second"].IsDisposed).IsFalse();
    }

    [Test]
    public async Task Count_TryRemove_And_Clear_Report_Each_Removal()
    {
        var evicted = new List<string>();
        var provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions
            {
                OnCreated = item => evicted.Add($"created:{item.Key}"),
                OnEvicted = item => evicted.Add($"evicted:{item.Key}:{item.Reason}"),
            });
        _ = provider.GetShield("first");
        _ = provider.GetShield("second");

        await Assert.That(provider.TryRemove("missing")).IsFalse();
        await Assert.That(await provider.TryRemoveAsync("first")).IsTrue();
        await provider.ClearAsync();

        await Assert.That(provider.Count).IsEqualTo(0);
        await Assert.That(provider.ClearedEvictionCount).IsEqualTo(2);
        await Assert.That(provider.EvictionCount).IsEqualTo(2);
        await Assert.That(evicted).IsEquivalentTo([
            "created:first",
            "created:second",
            "evicted:first:Cleared",
            "evicted:second:Cleared",
        ]);
    }

    [Test]
    public async Task Testing_Snapshot_Includes_Count_And_Evictions()
    {
        var provider = new PartitionedShield<int>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions { MaximumPartitions = 1 });
        _ = provider.GetShield(1);
        _ = provider.GetShield(2);
        var snapshot = provider.GetStateSnapshot();

        await Assert.That(snapshot.ContractVersion).IsEqualTo(1);
        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(snapshot.CreatedCount).IsEqualTo(2);
        await Assert.That(snapshot.CapacityEvictionCount).IsEqualTo(1);
        await Assert.That(snapshot.EvictionCount).IsEqualTo(1);
    }

    private sealed class DisposableResource : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}

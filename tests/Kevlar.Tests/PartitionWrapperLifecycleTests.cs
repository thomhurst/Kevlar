using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class PartitionWrapperLifecycleTests
{
    [Test]
    public async Task Typed_Partition_Wrapper_Exposes_Complete_Cache_Lifecycle()
    {
        var provider = new PartitionedShield<string, int>(
            static _ => Shield.For<int>().FallbackTo(42),
            new PartitionedShieldOptions { MaxPartitions = 2 });

        var first = provider.GetShield("first");
        _ = provider.GetShield("second");

        await Assert.That(provider.Count).IsEqualTo(2);
        await Assert.That(provider.CreatedCount).IsEqualTo(2);
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(0);
        await Assert.That(provider.ExpirationEvictionCount).IsEqualTo(0);
        await Assert.That(provider.PruneExpired()).IsEqualTo(0);
        await Assert.That(provider.TryRemove("missing")).IsFalse();
        await Assert.That(provider.TryRemove("first")).IsTrue();
        await Assert.That(provider.TryGetShield("first", out _)).IsFalse();

        var heldResult = await first.ExecuteAsync(static _ => new ValueTask<int>(1));
        await Assert.That(heldResult).IsEqualTo(1);

        provider.Clear();

        await Assert.That(provider.Count).IsEqualTo(0);
        await Assert.That(provider.CreatedCount).IsEqualTo(2);
    }

    [Test]
    public async Task Void_Partition_Wrapper_Exposes_Eviction_And_Clear_Counters()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = new PartitionedShield<string>(
            static _ => Shield.Fallback(static _ => ValueTask.CompletedTask),
            new PartitionedShieldOptions
            {
                MaxPartitions = 2,
                IdleExpiration = TimeSpan.FromMinutes(1),
                TimeProvider = timeProvider,
            });

        _ = provider.GetShield("first");
        _ = provider.GetShield("second");
        _ = provider.GetShield("third");

        await Assert.That(provider.Count).IsEqualTo(2);
        await Assert.That(provider.CreatedCount).IsEqualTo(3);
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(1);
        await Assert.That(provider.ExpirationEvictionCount).IsEqualTo(0);
        await Assert.That(provider.TryGetShield("first", out _)).IsFalse();
        await Assert.That(provider.TryGetShield("third", out var held)).IsTrue();

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await Assert.That(provider.PruneExpired()).IsEqualTo(2);
        await Assert.That(provider.ExpirationEvictionCount).IsEqualTo(2);
        await Assert.That(provider.Count).IsEqualTo(0);
        await Assert.That(provider.TryRemove("third")).IsFalse();

        await held!.ExecuteAsync(static _ => ValueTask.CompletedTask);
        _ = provider.GetShield("fourth");
        provider.Clear();

        await Assert.That(provider.Count).IsEqualTo(0);
        await Assert.That(provider.CreatedCount).IsEqualTo(4);
    }

    [Test]
    public async Task Removing_A_Middle_Lru_Entry_Preserves_Later_Eviction_Order()
    {
        var provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions { MaxPartitions = 4 });
        _ = provider.GetShield("a");
        _ = provider.GetShield("b");
        _ = provider.GetShield("c");
        _ = provider.GetShield("d");

        await Assert.That(provider.TryRemove("b")).IsTrue();
        _ = provider.GetShield("a");
        _ = provider.GetShield("e");
        _ = provider.GetShield("f");

        await Assert.That(provider.TryGetShield("c", out _)).IsFalse();
        await Assert.That(provider.TryGetShield("a", out _)).IsTrue();
        await Assert.That(provider.TryGetShield("d", out _)).IsTrue();
        await Assert.That(provider.TryGetShield("e", out _)).IsTrue();
        await Assert.That(provider.TryGetShield("f", out _)).IsTrue();
        await Assert.That(provider.Count).IsEqualTo(4);
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(1);
    }
}

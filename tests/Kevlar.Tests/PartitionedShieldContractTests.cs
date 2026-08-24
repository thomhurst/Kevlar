using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class PartitionedShieldContractTests
{
    [Test]
    public async Task TryGetShield_Refreshes_Lru_Order()
    {
        var provider = new PartitionedShield<string>(
            _ => Shield.Empty,
            new PartitionedShieldOptions { MaximumPartitions = 2 });
        var first = provider.GetShield("first");
        _ = provider.GetShield("second");

        var found = provider.TryGetShield("first", out var retained);
        _ = provider.GetShield("third");

        await Assert.That(found).IsTrue();
        await Assert.That(retained).IsSameReferenceAs(first);
        await Assert.That(provider.TryGetShield("first", out _)).IsTrue();
        await Assert.That(provider.TryGetShield("second", out _)).IsFalse();
        await Assert.That(provider.TryGetShield("third", out _)).IsTrue();
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(1);
    }

    [Test]
    public async Task TryGetShield_Refreshes_Idle_Expiration()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = new PartitionedShield<string>(
            _ => Shield.Empty,
            new PartitionedShieldOptions
            {
                IdleExpiration = TimeSpan.FromMinutes(1),
                TimeProvider = timeProvider,
            });
        var original = provider.GetShield("tenant");

        timeProvider.Advance(TimeSpan.FromSeconds(45));
        await Assert.That(provider.TryGetShield("tenant", out var touched)).IsTrue();
        timeProvider.Advance(TimeSpan.FromSeconds(45));

        await Assert.That(provider.PruneExpired()).IsEqualTo(0);
        await Assert.That(provider.TryGetShield("tenant", out var retained)).IsTrue();
        await Assert.That(touched).IsSameReferenceAs(original);
        await Assert.That(retained).IsSameReferenceAs(original);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await Assert.That(provider.PruneExpired()).IsEqualTo(1);
        await Assert.That(provider.TryGetShield("tenant", out _)).IsFalse();
        await Assert.That(provider.ExpirationEvictionCount).IsEqualTo(1);
    }

    [Test]
    public async Task Custom_Comparer_Coalesces_Equivalent_Keys()
    {
        var factoryKeys = new List<string>();
        var provider = new PartitionedShield<string>(
            key =>
            {
                factoryKeys.Add(key);
                return Shield.Retry(0, Backoff.None);
            },
            comparer: StringComparer.OrdinalIgnoreCase);

        var first = provider.GetShield("Tenant");
        var equivalent = provider.GetShield("TENANT");

        await Assert.That(equivalent).IsSameReferenceAs(first);
        await Assert.That(provider.Count).IsEqualTo(1);
        await Assert.That(provider.CreatedCount).IsEqualTo(1);
        await Assert.That(factoryKeys).IsEquivalentTo(["Tenant"]);
        await Assert.That(provider.Remove("tenant")).IsTrue();
        await Assert.That(provider.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Null_Factory_Result_Does_Not_Poison_Later_Creation()
    {
        var attempts = 0;
        var provider = new PartitionedShield<string>(_ =>
            Interlocked.Increment(ref attempts) == 1 ? null! : Shield.Empty);

        var failure = await Assert.That(() => provider.GetShield("tenant"))
            .Throws<InvalidOperationException>();

        await Assert.That(failure!.Message).IsEqualTo("The partition factory returned null.");
        await Assert.That(provider.Count).IsEqualTo(0);
        await Assert.That(provider.CreatedCount).IsEqualTo(0);

        var created = provider.GetShield("tenant");

        await Assert.That(created).IsSameReferenceAs(Shield.Empty);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(provider.Count).IsEqualTo(1);
        await Assert.That(provider.CreatedCount).IsEqualTo(1);
    }

    [Test]
    public async Task Factory_Exception_Does_Not_Poison_Later_Creation()
    {
        var expected = new InvalidOperationException("first creation failed");
        var attempts = 0;
        var provider = new PartitionedShield<string>(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw expected;
            }

            return Shield.Empty;
        });

        var failure = await Assert.That(() => provider.GetShield("tenant"))
            .Throws<InvalidOperationException>();

        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(provider.Count).IsEqualTo(0);
        await Assert.That(provider.CreatedCount).IsEqualTo(0);

        var created = provider.GetShield("tenant");

        await Assert.That(created).IsSameReferenceAs(Shield.Empty);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(provider.CreatedCount).IsEqualTo(1);
    }
}

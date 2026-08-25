using System.Collections.Concurrent;
using Kevlar;

var attempts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
var tenants = new PartitionedShield<string>(
    _ => Shield.When<InvalidOperationException>()
        .Retry(1, Backoff.None)
        .WithName("tenant-worker"),
    new PartitionedShieldOptions { MaximumPartitions = 16 },
    StringComparer.Ordinal);

foreach (var tenant in new[] { "alpha", "beta" })
{
    await tenants.GetShield(tenant).ExecuteAsync(_ =>
    {
        var attempt = attempts.AddOrUpdate(tenant, 1, static (_, count) => count + 1);
        return attempt == 1
            ? ValueTask.FromException(new InvalidOperationException("transient"))
            : ValueTask.CompletedTask;
    });
}

if (tenants.Count != 2 || tenants.CreatedCount != 2 || attempts.Values.Any(static count => count != 2))
{
    throw new InvalidOperationException("Expected two isolated tenant shields and one retry per tenant.");
}

Console.WriteLine("Partitioned worker sample passed for two isolated tenants.");

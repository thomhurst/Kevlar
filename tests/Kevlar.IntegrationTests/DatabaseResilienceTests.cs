using Kevlar.IntegrationTests.Infrastructure;

namespace Kevlar.IntegrationTests;

/// <summary>
/// Database-style scenarios: connection-pool protection with bulkheads, transient-error
/// retries, a circuit shared across repositories, and a full checkout-style mix.
/// </summary>
public class DatabaseResilienceTests
{
    [Test]
    public async Task Unprotected_Load_Exhausts_The_Pool_But_A_Bulkhead_Prevents_It()
    {
        // Control: 20 truly parallel queries against a 5-connection pool blow it up.
        var unprotected = new FlakyDatabase { MaxConnections = 5, Latency = TimeSpan.FromMilliseconds(50) };

        var failures = 0;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            try
            {
                await unprotected.QueryAsync("select 1", CancellationToken.None);
            }
            catch (DatabaseUnavailableException)
            {
                Interlocked.Increment(ref failures);
            }
        }));

        await Assert.That(failures > 0).IsTrue();

        // Same load through a bulkhead sized to the pool: everything succeeds.
        var guarded = new FlakyDatabase { MaxConnections = 5, Latency = TimeSpan.FromMilliseconds(50) };
        var policy = Policy.Bulkhead(maxConcurrency: 5, maxQueue: 15);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => policy.ExecuteAsync(ct => new ValueTask<string>(guarded.QueryAsync("select 1", ct))).AsTask()));

        await Assert.That(results.Length).IsEqualTo(20);
        await Assert.That(results.All(r => r.StartsWith("rows"))).IsTrue();
        await Assert.That(guarded.MaxObservedConcurrency <= 5).IsTrue();
    }

    [Test]
    public async Task Deadlock_Retries_Recover_Without_Retrying_Real_Faults()
    {
        var database = new FlakyDatabase();
        database.FailNextQueries(2);

        var policy = Policy
            .Handle<TransientDatabaseException>()
            .Retry(3, Backoff.Constant(TimeSpan.FromMilliseconds(5)));

        var result = await policy.ExecuteAsync(ct => new ValueTask<string>(database.QueryAsync("select o from orders", ct)));

        await Assert.That(result).IsEqualTo("rows(select o from orders)");
        await Assert.That(database.QueryCount).IsEqualTo(3);

        // A non-transient fault must not be retried by the same policy.
        database.SetOffline(true);
        await Assert.That(async () => await policy.ExecuteAsync(ct => new ValueTask<string>(database.QueryAsync("select 1", ct))))
            .Throws<DatabaseUnavailableException>();
        await Assert.That(database.QueryCount).IsEqualTo(4);
    }

    [Test]
    public async Task One_Circuit_Protects_Every_Repository_Sharing_It()
    {
        var database = new FlakyDatabase();
        database.SetOffline(true);

        var breaker = Policy
            .Handle<DatabaseUnavailableException>()
            .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromMinutes(1));

        var ordersPolicy = Policy.Handle<DatabaseUnavailableException>().Retry(1, Backoff.None).Wrap(breaker);
        var usersPolicy = Policy.Timeout(TimeSpan.FromSeconds(5)).Wrap(breaker);

        // Two failed attempts through the orders repository trip the shared circuit;
        // the exhausted retry surfaces the underlying fault.
        await Assert.That(async () => await ordersPolicy.ExecuteAsync(ct => new ValueTask<string>(database.QueryAsync("orders", ct))))
            .Throws<DatabaseUnavailableException>();

        var queriesAfterTrip = database.QueryCount;
        await Assert.That(queriesAfterTrip).IsEqualTo(2);

        // …so the users repository fails fast without touching the database at all.
        await Assert.That(async () => await usersPolicy.ExecuteAsync(ct => new ValueTask<string>(database.QueryAsync("users", ct))))
            .Throws<CircuitOpenException>();
        await Assert.That(database.QueryCount).IsEqualTo(queriesAfterTrip);
    }

    [Test]
    public async Task Checkout_Mix_Timeout_Retry_Breaker_Bulkhead()
    {
        var database = new FlakyDatabase { Latency = TimeSpan.FromMilliseconds(10) };
        database.FailNextQueries(2);

        var policy = Policy
            .Timeout(TimeSpan.FromSeconds(10))
            .Handle<TransientDatabaseException>()
            .Retry(3, Backoff.Constant(TimeSpan.FromMilliseconds(5)))
            .CircuitBreaker(10, TimeSpan.FromSeconds(30))
            .Bulkhead(maxConcurrency: 5, maxQueue: 20);

        var result = await policy.ExecuteAsync(ct => new ValueTask<string>(database.QueryAsync("insert order", ct)));

        await Assert.That(result).IsEqualTo("rows(insert order)");
        await Assert.That(database.QueryCount).IsEqualTo(3);
    }

    [Test]
    public async Task Hedged_Reads_Prefer_The_Fastest_Replica()
    {
        // Replica 1 is degraded (never answers); replica 2 is healthy.
        var slowStarted = new TaskCompletionSource();
        var attemptIndex = 0;

        var policy = Policy
            .Timeout(TimeSpan.FromSeconds(10))
            .Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(50));

        var result = await policy.ExecuteAsync(async ct =>
        {
            var replica = Interlocked.Increment(ref attemptIndex);

            if (replica == 1)
            {
                slowStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            }

            return $"replica-{replica}";
        });

        await Assert.That(result).IsEqualTo("replica-2");
        await slowStarted.Task; // the degraded replica really was attempted and then abandoned
    }
}

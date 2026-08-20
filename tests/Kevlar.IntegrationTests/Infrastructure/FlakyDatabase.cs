namespace Kevlar.IntegrationTests.Infrastructure;

internal sealed class TransientDatabaseException : Exception
{
    public TransientDatabaseException()
        : base("Deadlock victim; retry the transaction.")
    {
    }
}

internal sealed class DatabaseUnavailableException : Exception
{
    public DatabaseUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A fake database with a hard connection cap, real async latency, and scriptable failures,
/// standing in for SQL/NoSQL clients in integration tests.
/// </summary>
internal sealed class FlakyDatabase
{
    private int _transientFailuresRemaining;
    private bool _offline;
    private int _active;
    private int _maxObservedConcurrency;
    private int _queries;

    public int MaxConnections { get; init; } = 5;

    public TimeSpan Latency { get; init; } = TimeSpan.FromMilliseconds(25);

    public int QueryCount => Volatile.Read(ref _queries);

    public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

    /// <summary>Makes the next <paramref name="count"/> queries fail with <see cref="TransientDatabaseException"/>.</summary>
    public void FailNextQueries(int count) => Volatile.Write(ref _transientFailuresRemaining, count);

    /// <summary>Takes the database down (or brings it back). Offline queries fail after their latency.</summary>
    public void SetOffline(bool offline) => Volatile.Write(ref _offline, offline);

    public async Task<string> QueryAsync(string sql, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _queries);
        var active = Interlocked.Increment(ref _active);

        try
        {
            RecordConcurrency(active);

            if (active > MaxConnections)
            {
                throw new DatabaseUnavailableException("The connection pool is exhausted.");
            }

            await Task.Delay(Latency, cancellationToken);

            if (Volatile.Read(ref _offline))
            {
                throw new DatabaseUnavailableException("The database is offline.");
            }

            if (Interlocked.Decrement(ref _transientFailuresRemaining) >= 0)
            {
                throw new TransientDatabaseException();
            }

            return $"rows({sql})";
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    private void RecordConcurrency(int current)
    {
        int seen;
        while (current > (seen = Volatile.Read(ref _maxObservedConcurrency)))
        {
            Interlocked.CompareExchange(ref _maxObservedConcurrency, current, seen);
        }
    }
}

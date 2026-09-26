using System.Data.Common;
using Kevlar.Extensions.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Kevlar.Tests;

public class EntityFrameworkCoreTests
{
    private static Shield RetryShield => Shield.When<IOException>().Retry(1, Backoff.None);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Sqlite_Operations_Retry_Transient_Failures_And_Preserve_State(bool asynchronous)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection, RetryShield);
        await context.Database.EnsureCreatedAsync();
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        var state = new object();
        int Operation(DbContext database, object callerState)
        {
            if (!ReferenceEquals(database, context) || !ReferenceEquals(callerState, state))
            {
                throw new InvalidOperationException("Lost context or state");
            }

            if (++attempts == 1) { throw new IOException("Transient"); }
            context.Widgets.Add(new Widget { Id = 1 });
            return context.SaveChanges();
        }

        var result = asynchronous
            ? await strategy.ExecuteAsync(state, (database, value, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(Operation(database, value));
            }, verifySucceeded: null)
            : strategy.Execute(state, Operation, verifySucceeded: null);
        await Assert.That(result).IsEqualTo(1);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(await context.Widgets.CountAsync()).IsEqualTo(1);
        await Assert.That(strategy.RetriesOnFailure).IsTrue();
        await Assert.That(ExecutionStrategy.Current).IsNull();
    }

    [Test]
    public async Task NonTransient_Exception_Is_Not_Retried()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var context = CreateContext(connection, RetryShield);
        var calls = 0;
        var error = new InvalidOperationException("Permanent");
        var strategy = context.Database.CreateExecutionStrategy();
        var thrown = await Assert.That(() => strategy.Execute<object?, int>(null, (_, _) =>
        {
            calls++;
            throw error;
        }, verifySucceeded: null)).Throws<InvalidOperationException>();
        await Assert.That(thrown).IsSameReferenceAs(error);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(ExecutionStrategy.Current).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Commit_Verification_Prevents_Replaying_Committed_Transaction(bool asynchronous)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new AmbiguousCommitInterceptor();
        await using var context = CreateContext(connection, RetryShield, interceptor);
        await context.Database.EnsureCreatedAsync();
        interceptor.Enabled = true;
        var strategy = context.Database.CreateExecutionStrategy();
        var operations = 0;
        var verifications = 0;
        context.Widgets.Add(new Widget { Id = 10 });
        if (asynchronous)
        {
            await strategy.ExecuteInTransactionAsync(
                context,
                async (database, token) =>
                {
                    operations++;
                    return await database.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken: token);
                },
                async (database, token) =>
                {
                    verifications++;
                    return await database.Widgets.AsNoTracking().AnyAsync(widget => widget.Id == 10, token);
                });
        }
        else
        {
            strategy.ExecuteInTransaction(context,
                database => { operations++; return database.SaveChanges(acceptAllChangesOnSuccess: false); },
                database => { verifications++; return database.Widgets.AsNoTracking().Any(widget => widget.Id == 10); });
        }

        await Assert.That(operations).IsEqualTo(1);
        await Assert.That(verifications).IsEqualTo(1);
        await Assert.That(await context.Widgets.CountAsync()).IsEqualTo(1);
        await Assert.That(ExecutionStrategy.Current).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Failed_Verification_Never_Replays_An_Ambiguous_Operation(bool asynchronous)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var context = CreateContext(connection, RetryShield);
        var strategy = context.Database.CreateExecutionStrategy();
        var operations = 0;
        var verifications = 0;
        var failure = new IOException("Verification unavailable");
        int Operation() { operations++; throw new IOException("Ambiguous commit"); }
        ExecutionResult<int> Verify() { verifications++; throw failure; }
        Exception? observed = null;
        try
        {
            if (asynchronous)
            {
                await strategy.ExecuteAsync<object?, int>(null,
                    (_, _, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(Operation()); },
                    (_, _, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(Verify()); });
            }
            else
            {
                strategy.Execute<object?, int>(null, (_, _) => Operation(), (_, _) => Verify());
            }
        }
        catch (Exception exception) { observed = exception; }
        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(operations).IsEqualTo(1);
        await Assert.That(verifications).IsEqualTo(2);
        await Assert.That(ExecutionStrategy.Current).IsNull();
    }

    [Test]
    public async Task Negative_Verification_Allows_Retry_And_Returns_Real_Result()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var context = CreateContext(connection, RetryShield);
        var strategy = context.Database.CreateExecutionStrategy();
        var operations = 0;
        var verifications = 0;
        var result = await strategy.ExecuteAsync<object?, int>(null, (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (++operations == 1) { throw new IOException("Not committed"); }
            return Task.FromResult(42);
        }, (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            verifications++;
            return Task.FromResult(new ExecutionResult<int>(successful: false, result: -1));
        });
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(operations).IsEqualTo(2);
        await Assert.That(verifications).IsEqualTo(1);
    }

    [Test]
    public async Task Cancellation_Reaches_Operation_And_Does_Not_Verify()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var context = CreateContext(connection, RetryShield);
        using var cancellation = new CancellationTokenSource();
        var strategy = context.Database.CreateExecutionStrategy();
        var verifications = 0;
        await Assert.That(() => strategy.ExecuteAsync<object?, int>(null, async (_, _, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            verifications++;
            return Task.FromResult(new ExecutionResult<int>(true, 1));
        }, cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(verifications).IsEqualTo(0);
        await Assert.That(ExecutionStrategy.Current).IsNull();
    }

    [Test]
    public async Task Nested_Ef_Operations_Use_Only_Outer_Retry_Allowance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var context = CreateContext(connection, RetryShield);
        var outer = context.Database.CreateExecutionStrategy();
        var outerCalls = 0;
        var innerCalls = 0;
        var nestedRetries = true;
        var result = await outer.ExecuteAsync<object?, int>(null, async (_, _, token) =>
        {
            outerCalls++;
            var inner = context.Database.CreateExecutionStrategy();
            nestedRetries = inner.RetriesOnFailure;
            return await inner.ExecuteAsync<object?, int>(null, (_, _, innerToken) =>
            {
                innerToken.ThrowIfCancellationRequested();
                if (++innerCalls == 1) { throw new IOException("Transient"); }
                return Task.FromResult(42);
            }, verifySucceeded: null, token);
        }, verifySucceeded: null);
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(outerCalls).IsEqualTo(2);
        await Assert.That(innerCalls).IsEqualTo(2);
        await Assert.That(nestedRetries).IsFalse();
    }

    [Test]
    public async Task Active_Transaction_Must_Be_Created_Inside_Execution_Delegate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection, RetryShield);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var strategy = context.Database.CreateExecutionStrategy();
        await Assert.That(() => strategy.Execute<object?, int>(null, (_, _) => 1, verifySucceeded: null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Empty_Shield_Does_Not_Advertise_Retries_And_Unsafe_Shields_Are_Rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var context = CreateContext(connection, Shield.Empty);
        await Assert.That(context.Database.CreateExecutionStrategy().RetriesOnFailure).IsFalse();
        var dependencies = context.GetService<ExecutionStrategyDependencies>();
        await Assert.That(() => new KevlarExecutionStrategy(dependencies, Shield.Hedge(1, TimeSpan.Zero)))
            .Throws<NotSupportedException>();
        await Assert.That(() => new KevlarExecutionStrategy(dependencies, Shield.Timeout(TimeSpan.FromSeconds(1))))
            .Throws<NotSupportedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Provider_Defaults_Preserve_Transient_Classification_And_Configured_Retry_Count(bool postgres)
    {
        var options = new DbContextOptionsBuilder<TestContext>();
        if (postgres)
        {
            options.UseNpgsql("Host=localhost;Database=unused", provider =>
                provider.EnableRetryOnFailure(maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero, errorCodesToAdd: null));
        }
        else
        {
            options.UseSqlServer("Server=localhost;Database=unused;User Id=unused;Password=unused", provider =>
                provider.EnableRetryOnFailure(maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero, errorNumbersToAdd: null));
        }

        options.UseKevlarExecutionStrategy();
        await using var context = new TestContext(options.Options);
        var strategy = context.Database.CreateExecutionStrategy();
        var calls = 0;
        var transient = postgres
            ? (Exception)new PostgresException("serialization", "ERROR", "ERROR", "40001")
            : new TimeoutException("SQL transient");
        var observed = await Assert.That(() => strategy.Execute<object?, int>(null, (_, _) =>
        {
            calls++;
            throw transient;
        }, verifySucceeded: null)).Throws<Exception>();
        await Assert.That(observed).IsSameReferenceAs(transient);
        await Assert.That(calls).IsEqualTo(2);
        calls = 0;
        await Assert.That(() => strategy.Execute<object?, int>(null, (_, _) =>
        {
            calls++;
            throw new ArgumentException("Permanent");
        }, verifySucceeded: null)).Throws<ArgumentException>();
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Default_Policy_Requires_Supported_Provider_Or_Explicit_Shield()
    {
        var options = new DbContextOptionsBuilder<TestContext>().UseSqlite("Data Source=:memory:")
            .UseKevlarExecutionStrategy();
        await using var context = new TestContext(options.Options);
        await Assert.That(() => context.Database.CreateExecutionStrategy()).Throws<NotSupportedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Unconfigured_Provider_Defaults_Unwrap_Transient_Database_Errors(bool postgres)
    {
        var options = new DbContextOptionsBuilder<TestContext>();
        if (postgres) { options.UseNpgsql("Host=localhost;Database=unused"); }
        else { options.UseSqlServer("Server=localhost;Database=unused;Integrated Security=true"); }
        options.UseKevlarExecutionStrategy();
        await using var context = new TestContext(options.Options);
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        var transient = postgres
            ? (Exception)new PostgresException("serialization", "ERROR", "ERROR", "40001")
            : new TimeoutException("SQL transient");
        var result = await strategy.ExecuteAsync<object?, int>(null, async (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            if (++attempts == 1) { throw new DbUpdateException("Wrapped failure", transient); }
            return 42;
        }, verifySucceeded: null);
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Provider_Delay_Receives_One_Based_Failure_Count_And_Resets_Between_Executions()
    {
        var counts = new List<int>();
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseSqlite("Data Source=:memory:", provider => provider.ExecutionStrategy(
                dependencies => new RecordingExecutionStrategy(dependencies, counts)))
            .UseKevlarExecutionStrategy();
        await using var context = new TestContext(options.Options);
        var strategy = context.Database.CreateExecutionStrategy();
        for (var execution = 0; execution < 2; execution++)
        {
            var attempts = 0;
            var result = strategy.Execute<object?, int>(null, (_, _) =>
            {
                if (++attempts <= 2) { throw new IOException("Transient"); }
                return 42;
            }, verifySucceeded: null);
            await Assert.That(result).IsEqualTo(42);
        }

        await Assert.That(counts).IsEquivalentTo(new[] { 1, 2, 1, 2 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Verification_Filter_Can_Exclude_An_Exception()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var context = CreateContext(connection, RetryShield);
        var strategy = new KevlarExecutionStrategy(context.GetService<ExecutionStrategyDependencies>(),
            RetryShield, shouldVerifySuccessOn: static _ => false);
        var attempts = 0;
        var verified = false;
        await Assert.That(() => strategy.Execute<object?, int>(null, (_, _) =>
        {
            attempts++;
            throw new IOException("Not eligible for verification");
        }, (_, _) => { verified = true; return new ExecutionResult<int>(true, 1); })).Throws<IOException>();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(verified).IsFalse();
    }

    [Test]
    public async Task Cancellation_During_Retry_Delay_Stops_Further_Database_Operations()
    {
        using var cancellation = new CancellationTokenSource();
        var shield = Shield.When<IOException>().Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.Constant(TimeSpan.FromHours(1));
            options.OnRetry = _ => { cancellation.Cancel(); return default; };
        });
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var context = CreateContext(connection, shield);
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        await Assert.That(() => strategy.ExecuteAsync<object?, int>(null, (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            attempts++;
            throw new IOException("Transient");
        }, verifySucceeded: null, cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    private sealed class RecordingExecutionStrategy(ExecutionStrategyDependencies dependencies, List<int> counts)
        : ExecutionStrategy(dependencies, maxRetryCount: 2, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is IOException;

        protected override TimeSpan? GetNextDelay(Exception lastException)
        {
            counts.Add(ExceptionsEncountered.Count);
            return TimeSpan.Zero;
        }
    }

    private static TestContext CreateContext(SqliteConnection connection, Shield shield, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<TestContext>().UseSqlite(connection)
            .AddInterceptors(interceptors).UseKevlarExecutionStrategy(shield).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
    }

    private sealed class Widget
    {
        public int Id { get; set; }
    }

    private sealed class AmbiguousCommitInterceptor : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (Enabled) { throw new IOException("Commit succeeded but acknowledgement was lost"); }
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TransactionCommitted(transaction, eventData);
            return Task.CompletedTask;
        }
    }
}

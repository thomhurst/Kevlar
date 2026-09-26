---
sidebar_position: 12
---

# Entity Framework Core

`Kevlar.Extensions.EntityFrameworkCore` plugs a sequential shield into EF Core's
`IExecutionStrategy`. It supports .NET 8 and .NET 10 with EF Core 8.0.11 or later.
Install your relational provider separately, and configure that provider before Kevlar.

```bash
dotnet add package Kevlar.Extensions.EntityFrameworkCore
```

## Provider defaults

SQL Server and Npgsql can supply their own transient-error classification and retry delays.
Configure `EnableRetryOnFailure` first to retain its retry count, delay cap, and additional
error codes; then call `UseKevlarExecutionStrategy` without a shield:

```csharp
using System;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder()
    .UseSqlServer("Server=localhost;Database=orders;Integrated Security=true",
        sql => sql.EnableRetryOnFailure(
            maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null))
    .UseKevlarExecutionStrategy();
```

With no configured retry factory, the adapter creates the installed SQL Server or Npgsql
provider's default retry policy. Provider discovery uses reflection over protected policy
hooks and requires untrimmed provider assemblies. The package does not install either provider
and does not promise Native AOT compatibility. An explicit shield avoids this discovery;
EF Core and provider trimming restrictions still apply.

Other providers require an explicit shield. Do not retry every database exception: constraint
violations and invalid queries usually need application changes, not another attempt.

## Explicit policy

The following SQLite example uses an `IOException` predicate only for simulated transient
failures. Select the appropriate provider-specific predicate in production.

```csharp
using System.IO;
using Kevlar;
using Microsoft.EntityFrameworkCore;

var shield = Shield.When<IOException>().Retry(2, backoff: Backoff.None);
var options = new DbContextOptionsBuilder()
    .UseSqlite("Data Source=:memory:")
    .UseKevlarExecutionStrategy(shield);
```

You can also configure the provider's execution strategy factory directly:

```csharp
using System;
using Kevlar;
using Kevlar.Extensions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

var shield = Shield.When<TimeoutException>().Retry(2, backoff: Backoff.None);
var options = new DbContextOptionsBuilder()
    .UseSqlServer("Server=localhost;Database=orders;Integrated Security=true",
        sql => sql.ExecutionStrategy(dependencies => new KevlarExecutionStrategy(dependencies, shield)));
```

The adapter belongs to one `DbContext`; do not execute it concurrently. It rejects hedging,
timeout strategies that can abandon active database work, custom strategies without an
at-most-once continuation guarantee, and live-forwarding shields. Use command timeouts and
operation cancellation tokens for database time limits. Fixed shields may be shared across
contexts; stateful strategies then share their state as usual. Avoid fallback policies that
turn a failed write into apparent success.

## Transactions and commit verification

Use `database.Database.CreateExecutionStrategy()` and EF Core's `ExecuteInTransactionAsync`
(or synchronous `ExecuteInTransaction`) to put the whole transaction inside the retry boundary.
Do not begin a transaction outside a retrying execution delegate. EF Core rejects that ordering.
Nested EF operations reuse the active execution scope and do not multiply retry allowances.

This example writes a client-generated identifier, preserves tracked changes until commit,
and checks for the identifier when the commit acknowledgement is lost:

<!-- doc-test-tail-declaration: split-before=public sealed class OrdersContext -->
```csharp
using System;
using System.IO;
using Kevlar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<OrdersContext>()
    .UseSqlite(connection)
    .UseKevlarExecutionStrategy(Shield.When<IOException>().Retry(2, backoff: Backoff.None));
await using var database = new OrdersContext(options.Options);
await database.Database.EnsureCreatedAsync();
var orderId = Guid.NewGuid();
database.Orders.Add(new Order { Id = orderId });

await database.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(database,
    (context, token) => context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken: token),
    (context, token) => context.Orders.AsNoTracking().AnyAsync(order => order.Id == orderId, token));
database.ChangeTracker.AcceptAllChanges();

public sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class Order
{
    public Guid Id { get; set; }
}
```

A successful verification returns the committed result without replaying the operation.
A negative verification permits normal retry classification. Verification queries use their
own shield retry allowance; if verification itself exhausts retries, its exception escapes and
the ambiguous operation is not replayed. The caller's cancellation token reaches operations,
verification queries, and retry delays. Cancellation is never treated as an ambiguous commit.

Provider defaults use the provider's verification filter. With an explicit shield, any
non-cancellation exception can reach a supplied verification callback. The direct constructor's
`shouldVerifySuccessOn` argument can narrow that filter. The shield still controls which failures
are retried. Write verification queries that distinguish a committed write from an unrelated row;
client-generated keys and idempotent writes help avoid duplicates.

See the [runnable SQLite sample](https://github.com/thomhurst/Kevlar/tree/main/samples/EntityFrameworkCore)
and [EF Core connection resiliency guidance](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).

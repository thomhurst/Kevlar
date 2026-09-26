# Kevlar.Extensions.EntityFrameworkCore

Sequential Kevlar execution strategies for Entity Framework Core on .NET 8 and .NET 10.
Requires EF Core 8.0.11 or later and a separately installed relational provider.

```csharp
using System.IO;
using Kevlar;
using Microsoft.EntityFrameworkCore;

var shield = Shield.When<IOException>().Retry(2, backoff: Backoff.None);
var options = new DbContextOptionsBuilder()
    .UseSqlite("Data Source=:memory:")
    .UseKevlarExecutionStrategy(shield);
```

The example requires `Microsoft.EntityFrameworkCore.Sqlite`; its `IOException` predicate is
for simulated failures. SQL Server and Npgsql applications can call `UseKevlarExecutionStrategy()`
after configuring the provider to use its native transient-error classification and retry delays.
Configure `EnableRetryOnFailure` first to preserve customized provider retry settings. Automatic
policy discovery requires untrimmed provider assemblies; other providers need an explicit shield.

Use EF Core's `CreateExecutionStrategy().ExecuteInTransactionAsync(...)` for a transaction with
commit verification. Successful verification prevents duplicate execution. A negative verification
result permits retry when the original failure is retryable; a verification query that exhausts its
retries terminates without replaying the ambiguous write. Caller cancellation flows through operations,
verification, and delays. Explicit shields verify any non-cancellation failure by default when a
verification callback is present; the direct constructor accepts `shouldVerifySuccessOn` to narrow it.

Each strategy belongs to one `DbContext` and must not run concurrently. Hedging, timeout strategies,
live-forwarding shields, and custom repetition are rejected. Use database command timeouts and
cancellation instead. Nested EF operations reuse the active scope. Retrying strategies reject
transactions opened outside their execution delegate.

[Documentation](https://github.com/thomhurst/Kevlar/blob/main/docs/docs/entity-framework-core.md)
contains provider setup, direct `KevlarExecutionStrategy` registration, and a complete transaction example.

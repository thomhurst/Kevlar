# EF Core execution strategy sample

Run SQLite in memory with a sequential Kevlar retry policy. The sample simulates one transient failure, retries the transaction, and checks that exactly one order commits. It also supplies EF Core's commit-verification callback and retains tracked changes until success.

```sh
dotnet run --project samples/EntityFrameworkCore -c Release -f net10.0 -- --smoke
```

No database server is required. `--smoke` runs the same deterministic scenario on .NET 8 and .NET 10 in CI. The `IOException` predicate is specific to this simulated failure; real applications must select their provider's transient errors.

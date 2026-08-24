# Console retry

A minimal console application retries two transient failures with zero backoff and verifies that the third attempt recovers. Run `dotnet run --project samples/ConsoleRetry -f net10.0`; add `-- --smoke` when using the repository smoke convention.

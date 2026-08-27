# Console retry

A minimal console application retries two transient failures with zero backoff and verifies that the third attempt recovers. Run `dotnet run --project samples/ConsoleRetry -f net10.0`; add `-- --smoke` when using the repository smoke convention.

Start here when learning Kevlar's fluent pipeline order. The sample declares the handled exception,
adds retry, and keeps the protected delegate cancellation-aware. `Backoff.None` makes the example
deterministic and fast; production code should normally select bounded constant or exponential
backoff with jitter. Change the retry count or remove the handling clause to see how the terminal
exception changes.

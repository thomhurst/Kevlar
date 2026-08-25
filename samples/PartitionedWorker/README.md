# Partitioned worker

This worker-style console application retains one bounded shield per tenant and proves that two tenants retry independently. Run `dotnet run --project samples/PartitionedWorker -f net10.0`; add `-- --smoke` for CI-style execution.

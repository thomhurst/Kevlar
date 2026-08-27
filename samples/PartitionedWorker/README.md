# Partitioned worker

This worker-style console application retains one bounded shield per tenant and proves that two tenants retry independently. Run `dotnet run --project samples/PartitionedWorker -f net10.0`; add `-- --smoke` for CI-style execution.

Use this pattern when one global breaker or retry history would let a noisy tenant affect every
other tenant. The partition key selects isolated strategy state, while `PartitionedShieldOptions<TKey>`
bounds retained entries so cardinality cannot grow forever. The sample uses two fixed tenant names;
production callers should choose stable, low-cardinality keys and observe eviction when traffic is
highly dynamic.

# Chaos-enabled integration smoke

This deterministic smoke confines a single injected fault to one operation, then verifies that retry recovers and the real action runs exactly once. Run `dotnet run --project samples/ChaosTesting -f net10.0`; add `-- --smoke` for CI.

The injection decision is controlled rather than random, making the program suitable for automated
tests and local debugging. Notice that chaos is composed inside retry: the injected failure becomes
the first handled attempt, then the real operation succeeds. Keep chaos disabled by default in
production, scope experiments narrowly, and emit enough telemetry to distinguish injected failures
from genuine dependency faults.

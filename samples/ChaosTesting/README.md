# Chaos-enabled integration smoke

This deterministic smoke confines a single injected fault to one operation, then verifies that retry recovers and the real action runs exactly once. Run `dotnet run --project samples/ChaosTesting -f net10.0`; add `-- --smoke` for CI.

---
sidebar_position: 15
---

# Testing Your Shields

## Inspecting pipeline shape

Install `Kevlar.Testing` in the test project only:

```shell
dotnet add package Kevlar.Testing
```

`GetDescriptor()` returns an immutable snapshot. Strategy kinds, execution order, and the typed configuration properties are stable contracts. `Description` is diagnostic text only; display it in failures, but do not parse it. Descriptors never expose a mutable strategy, callback, monitor, or `TimeProvider` instance.

In a TUnit `[Test]`, use the framework-independent shape assertions alongside TUnit's value assertions:

<!-- doc-test-ignore: The executable documentation harness owns Main; this TUnit example is compiled by Kevlar.Testing.Tests. -->
```csharp
var shield = Shield
    .Timeout(TimeSpan.FromSeconds(2))
    .Retry(3, Backoff.Constant(TimeSpan.FromMilliseconds(50)))
    .WithName("catalog");

var descriptor = shield.GetDescriptor()
    .AssertStrategyCount(2)
    .AssertStrategyOrder(StrategyKind.Timeout, StrategyKind.Retry);

var retry = descriptor.AssertContainsSingle<RetryStrategyDescriptor>();
await TUnit.Assertions.Assert.That(retry.MaxRetries).IsEqualTo(3);
await TUnit.Assertions.Assert.That(descriptor.Name).IsEqualTo("catalog");
```

`AssertContains<TDescriptor>()` checks presence when repeats are valid. `AssertContainsSingle<TDescriptor>()` also rejects duplicates. All shape failures throw `ShieldAssertionException` with expected and actual pipeline details.

## Recording telemetry and callbacks

`TelemetryRecorder` is an opt-in test listener. It captures immutable snapshots of Kevlar meter
measurements and exposes `Record` overloads that can be assigned directly to strategy callbacks.
The recorder copies metric tags and callback values immediately; it never retains a pooled
`KevlarContext`.

<!-- doc-test-ignore: The executable documentation harness owns Main; this TUnit example is compiled by Kevlar.Testing.Tests. -->
```csharp
using var telemetry = new TelemetryRecorder();
var shield = Shield.Retry(options =>
{
    options.MaxRetries = 1;
    options.Backoff = Backoff.None;
    options.OnRetry = telemetry.Record;
}).WithName("catalog");

await shield.ExecuteOutcomeAsync<int>(static _ =>
    ValueTask.FromException<int>(new InvalidOperationException("offline")));
await telemetry.WaitForCallbackCountAsync(1);

var retry = telemetry.Callbacks.Single();
await TUnit.Assertions.Assert.That(retry.Kind).IsEqualTo(CallbackKind.Retry);
await TUnit.Assertions.Assert.That(retry.RetryNumber).IsEqualTo(1);
await TUnit.Assertions.Assert.That(retry.ShieldName).IsEqualTo("catalog");

var execution = telemetry.Metrics.Single(record =>
    record.InstrumentName == "kevlar.executions");
await TUnit.Assertions.Assert.That(
    execution.Tags["kevlar.execution.outcome"]).IsEqualTo("failure");
```

The callback overloads cover typed and untyped retries and fallbacks, plus timeout, hedge, and
circuit-transition notifications. `WaitForMetricCountAsync` and `WaitForCallbackCountAsync` are
cancellation-aware, so tests can await concurrent pipelines without polling. Metric records retain
the documented low-cardinality tags; unnamed shields simply omit `kevlar.shield.name`.

Dispose the recorder at the end of each test to detach its `MeterListener`. Metric capture is
available on .NET 8 and later, matching core telemetry; callback recording remains available on
`netstandard2.0`. Construct with `captureMetrics: false` when a test needs callbacks only.

## Probing live resilience state

`GetStateSnapshot()` returns an immutable snapshot of circuit-breaker, rate-limiter, and
concurrency-limiter state. The snapshot contract is explicitly versioned; `ContractVersion == 1`
contains only stable state values and the original strategy index, never mutable strategy objects.
Stateless strategies are omitted.

<!-- doc-test-ignore: The executable documentation harness owns Main; this TUnit example is compiled by Kevlar.Testing.Tests. -->
```csharp
var shield = Shield.ConcurrencyLimit(1, queueLimit: 1);
var probe = new ExecutionProbe();

await shield.ExecuteAsync(probe.Wrap(static _ => ValueTask.CompletedTask));

var state = shield.GetStateSnapshot();
var limiter = state.Strategies.OfType<ConcurrencyLimitStateSnapshot>().Single();
await TUnit.Assertions.Assert.That(state.ContractVersion).IsEqualTo(1);
await TUnit.Assertions.Assert.That(limiter.AvailablePermits).IsEqualTo(1);
await TUnit.Assertions.Assert.That(probe.AttemptCount).IsEqualTo(1);
```

`ExecutionProbe.Wrap` supports typed and untyped asynchronous delegates. It counts each delegate
invocation as an attempt and records cancellation only while that attempt is active.
`WaitForAttemptCountAsync` and `WaitForCancellationCountAsync` let concurrent tests synchronize
without parsing diagnostics or adding hooks to production pipelines.

Snapshots are observations, not reservations: another execution may change state immediately after
capture. Composed shields that share a stateful strategy report that same underlying state. Rate
limits use the shield's configured `TimeProvider`, so tests can advance `FakeTimeProvider` before
capturing replenished availability.

## Repository quality gates

Pull requests build on Windows and Linux, then run the unit, `netstandard2.0` compatibility-asset, `netstandard2.1` gRPC compatibility-asset, chaos, integration, analyzer, testing-package, and rate-limiting-adapter suites independently with a five-minute timeout. Each suite has a project-specific minimum discovered-test floor near its current size, so partial discovery and accidental suite removal fail alongside empty runs. CI also verifies that every TUnit project is registered in the solution and CI workflow. Changes to core sources or tests run the expanded deterministic model sweep and a 30-second concurrency stress smoke test before merge; scheduled stress runs retain the full 15-minute duration.

The Linux coverage job merges all eight suites into Cobertura XML and an HTML report. It excludes test assemblies, benchmarks, generated code, and code marked with `ExcludeFromCodeCoverageAttribute`, then enforces baselines of 94% line coverage and 89% branch coverage. Download the `coverage-report` workflow artifact to inspect either format.

Core strategy mutation testing runs for relevant pull requests, every Monday at 03:23 UTC, and on demand. It uses the checked-in Stryker configuration, keeps 74% as the report reference, and enforces a 72% break threshold. That margin remains below the lowest observed unchanged run while blocking material mutation-coverage regressions. Superseded runs are cancelled, and completed runs publish HTML and JSON reports as the `mutation-report` artifact. The audited initial survivors and threshold policy are recorded in `.github/mutation-baseline.md`. Run the test and mutation checks locally with:

```powershell
dotnet tool restore
dotnet build Kevlar.slnx -c Release
dotnet run --project tests/Kevlar.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.Chaos.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.NetStandard.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.NetStandard21.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.IntegrationTests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.Analyzers.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.Testing.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.Extensions.RateLimiting.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
$coverageRoot = (New-Item -ItemType Directory -Force artifacts/coverage/raw).FullName
dotnet run --project tests/Kevlar.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/unit.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.Chaos.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/chaos.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.NetStandard.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/netstandard.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.NetStandard21.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/netstandard21.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.IntegrationTests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/integration.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.Analyzers.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/analyzers.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.Testing.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/testing.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.Extensions.RateLimiting.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/rate-limiting.cobertura.xml" --coverage-output-format cobertura
dotnet reportgenerator '-reports:artifacts/coverage/raw/*.cobertura.xml' '-targetdir:artifacts/coverage/report' '-reporttypes:Cobertura;Html'
./.github/scripts/Assert-Coverage.ps1 -Report artifacts/coverage/report/Cobertura.xml -MinimumLinePercent 94 -MinimumBranchPercent 89
Push-Location src/Kevlar
dotnet stryker --config-file stryker-config.json
Pop-Location
```

## Documentation snippet gates

Every C# fence in the README and Docusaurus documentation is compiled on pull requests. The harness extracts source directly from Markdown, generates isolated call sites, and restores Kevlar from the locally packed `.nupkg` files. This keeps documentation as the single source of truth and catches stale package IDs, API names, and overloads.

Complete snippets need no marker. A fragment that intentionally depends on omitted application code must place an explicit reason immediately before its fence:

```markdown
<!-- doc-test-ignore: Application transport implementation is omitted. -->
```

Class-level declarations use `doc-test-declaration`; mixed blocks can split declarations from call sites with `doc-test-tail-declaration`; isolated custom strategy members use `doc-test-strategy-member`; safe behavioral samples use `doc-test-run`. Unknown or malformed directives fail validation. Shell `dotnet add package` IDs and `dotnet run --project` paths are validated separately.

After packing with a chosen version, run the same package-consumer check locally:

```powershell
./scripts/Verify-DocSnippets.ps1 -PackagesPath artifacts/package/release -Version $packageVersion -NoImplicitUsings
```

Every delay, timeout and time window in Kevlar runs on a `TimeProvider`. Swap in a fake and your tests never actually wait:

```csharp
using Microsoft.Extensions.Time.Testing;   // Microsoft.Extensions.TimeProvider.Testing package

var time = new FakeTimeProvider();
var shield = Shield
    .Retry(3, Backoff.Constant(TimeSpan.FromSeconds(10)))
    .WithTimeProvider(time);

var pending = shield.ExecuteAsync(ct => FlakyAsync(ct)).AsTask();

time.Advance(TimeSpan.FromSeconds(10));   // first retry delay elapses instantly
time.Advance(TimeSpan.FromSeconds(10));   // second
time.Advance(TimeSpan.FromSeconds(10));   // third

var result = await pending;
```

`WithTimeProvider` returns a new shield (shields are immutable) with every time-dependent strategy — retry backoff, timeouts, circuit breaker break durations and sampling windows, rate-limit windows, hedging delays — driven by the provider you supply.

Copies still share stateful strategies. Circuit breakers and rate limiters normalize each provider's monotonic timestamp onto one elapsed-time timeline, so copies can safely use providers with different UTC epochs. Move time with the provider's normal advance mechanism; changing UTC alone does not advance break durations or sampling windows.

:::warning Advance *after* the execution is in flight
Start the execution first (note the `.AsTask()` without `await`), *then* advance time. If you advance before the shield has scheduled its delay, there's nothing to advance past and the pending task will hang waiting for a tick that already happened.
:::

### Bounded deterministic helpers

On .NET 8 and later, `Kevlar.Testing` references `Microsoft.Extensions.TimeProvider.Testing` and adds bounded helpers for pending executions and fake-time advancement. Conditions read only state owned by your test; the helpers never expose or retain a pooled `KevlarContext` or mutable strategy object.

<!-- doc-test-ignore: TUnit owns test discovery; this complete example is covered by Kevlar.Testing.Tests. -->
```csharp
using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

[Test]
public async Task Retries_Without_Sleeps()
{
    var time = new FakeTimeProvider();
    var attempts = 0;
    var shield = Shield
        .Retry(2, Backoff.Constant(TimeSpan.FromSeconds(10)))
        .WithTimeProvider(time);
    var execution = shield.ExecuteAsync<int>(_ =>
    {
        var attempt = Interlocked.Increment(ref attempts);
        return attempt < 3
            ? ValueTask.FromException<int>(new InvalidOperationException())
            : new ValueTask<int>(42);
    }).AsTask();

    await execution.WaitForPendingAsync(
        () => Volatile.Read(ref attempts) == 1,
        "the first retry delay");
    await time.AdvanceUntilAsync(
        TimeSpan.FromSeconds(10),
        () => Volatile.Read(ref attempts) == 3,
        "all retry attempts",
        maxAdvances: 2);

    await Assert.That(await execution).IsEqualTo(42);
}
```

Both helpers use finite scheduler/advance counts. `WaitForPendingAsync` reports the condition, execution status, and scheduler-yield bound when progress is not observed. `AdvanceUntilAsync` reports the condition, fake UTC time, and advance bound. Use a caller-owned counter, gate, callback flag, or fake-clock deadline as the condition; then await the execution normally to inspect its result or exception.

## Testing circuit breakers

Drive the breaker through its state machine without real clocks:

```csharp
var time = new FakeTimeProvider();
var monitor = new CircuitBreakerMonitor();
var shield = Shield
    .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromSeconds(30))
    .WithTimeProvider(time);

// Trip it:
for (var i = 0; i < 2; i++)
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => shield.ExecuteAsync(ct => throw new InvalidOperationException()).AsTask());

// Now open — rejects instantly:
await Assert.ThrowsAsync<CircuitOpenException>(
    () => shield.ExecuteAsync(ct => SucceedAsync(ct)).AsTask());

// After the break duration, one probe is allowed:
time.Advance(TimeSpan.FromSeconds(30));
var result = await shield.ExecuteAsync(ct => SucceedAsync(ct));   // closes the circuit
```

## Forcing outcomes without a real dependency

Shields don't care where the delegate's failures come from, so a plain counter is usually all the fake you need:

```csharp
var calls = 0;
var shield = Shield.Retry(2);

var result = await shield.ExecuteAsync(ct =>
{
    calls++;
    if (calls < 3) throw new HttpRequestException("boom");
    return new ValueTask<string>("ok");
});

// calls == 3: initial attempt + 2 retries
```

For assertion-friendly, no-throw checks, use [`ExecuteOutcomeAsync`](executing.md#no-throw-execution) and inspect the `Outcome<T>` instead of catching.

## Publish compatibility

Package verification publishes and runs clean package-consuming applications through
trimmed, single-file, and NativeAOT configurations. The matrix covers all Kevlar
strategies, typed and untyped execution, configuration-bound dependency injection,
and HTTP and gRPC extension integration on .NET 10. A trimmed .NET 8 application provides the
compatibility baseline. NativeAOT runs on Linux CI; the script reports unsupported
local platforms explicitly.

Run the complete package check after packing a local version:

On Linux, install the NativeAOT prerequisites first:

```bash
sudo apt-get update && sudo apt-get install --yes clang zlib1g-dev
```

```powershell
dotnet pack Kevlar.slnx -c Release -p:Version=0.0.0-local
./scripts/Verify-Packages.ps1 -PackagesPath artifacts/package/release -Version 0.0.0-local
```

---
sidebar_position: 10
---

# Testing Your Shields

## Repository quality gates

Pull requests build on Windows and Linux, then run the unit, netstandard2.0 asset, integration, and analyzer suites independently with a five-minute timeout. Every suite requires at least one discovered test, so a runner or discovery regression cannot pass as an empty run.

The Linux coverage job merges all four suites into Cobertura XML and an HTML report. It excludes test assemblies, benchmarks, generated code, and code marked with `ExcludeFromCodeCoverageAttribute`, then enforces the measured baselines of 92% line coverage and 86% branch coverage. Download the `coverage-report` workflow artifact to inspect either format.

Core strategy mutation testing runs for pull requests that change strategy code or unit tests, every Monday, and on demand. It uses the checked-in Stryker configuration and fails below the measured 74% mutation-score floor. The workflow has a 30-minute limit and publishes HTML and JSON reports as the `mutation-report` artifact. The audited initial survivors and ratchet policy are recorded in `.github/mutation-baseline.md`. Run the test and mutation gates locally with:

```powershell
dotnet tool restore
dotnet build Kevlar.slnx -c Release
dotnet run --project tests/Kevlar.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.NetStandard.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.IntegrationTests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
dotnet run --project tests/Kevlar.Analyzers.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict
$coverageRoot = (New-Item -ItemType Directory -Force artifacts/coverage/raw).FullName
dotnet run --project tests/Kevlar.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/unit.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.NetStandard.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/netstandard.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.IntegrationTests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/integration.cobertura.xml" --coverage-output-format cobertura
dotnet run --project tests/Kevlar.Analyzers.Tests -c Release --no-build -- --timeout 5m --minimum-expected-tests 1 --zero-tests-policy strict --coverage --coverage-settings .github/coverage.runsettings --coverage-output "$coverageRoot/analyzers.cobertura.xml" --coverage-output-format cobertura
dotnet reportgenerator '-reports:artifacts/coverage/raw/*.cobertura.xml' '-targetdir:artifacts/coverage/report' '-reporttypes:Cobertura;Html'
./.github/scripts/Assert-Coverage.ps1 -Report artifacts/coverage/report/Cobertura.xml -MinimumLinePercent 92 -MinimumBranchPercent 86
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
./scripts/Verify-DocSnippets.ps1 -PackagesPath artifacts/package/release -Version $packageVersion
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

---
sidebar_position: 7
---

# Dependency Injection

The `Kevlar.Extensions.DependencyInjection` package registers named shields with `Microsoft.Extensions.DependencyInjection` and exposes them through a registry and as keyed services.

```bash
dotnet add package Kevlar.Extensions.DependencyInjection
```

## Registering shields

```csharp
using Kevlar;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// A shield instance, under a name:
services.AddShield("github",
    Shield.Timeout(TimeSpan.FromSeconds(10)).Retry(3));

// A typed shield, built from the service provider (loggers, config, etc.):
services.AddShield<HttpResponseMessage>("downstream",
    sp => HttpShield.WhenTransient().Retry(3).WithName("downstream"));
```

Because shields are immutable and thread-safe, each named shield is a singleton — which is exactly what you want: every consumer of `"github"` shares the same instance, and therefore the same circuit breaker state, rate-limit bucket and concurrency limit slots.

## Consuming via the registry

<!-- doc-test-ignore: Application client type requires the host's FetchUserAsync implementation. -->
```csharp
public sealed class GitHubClient(IKevlarRegistry registry)
{
    private readonly Shield _shield = registry.GetShield("github");

    public Task<User> GetUserAsync(string id, CancellationToken ct) =>
        _shield.ExecuteAsync(ct2 => FetchUserAsync(id, ct2), ct).AsTask();
}
```

Typed shields come back through `GetShield<T>(name)`:

```csharp
var httpShield = registry.GetShield<HttpResponseMessage>("downstream");
```

`GetShield` throws a `KeyNotFoundException` (with an actionable message) for unknown names; `TryGetShield(name, out var shield)` / `TryGetShield<T>(...)` are the non-throwing forms.

Registry semantics worth knowing:

- Shield names are unique across untyped and result-aware registrations. A duplicate throws during
  registration. Pass `replace: true` explicitly when replacement is intentional.
- Ordinary factory registrations run lazily on first resolve and the result is cached — so every consumer shares one instance (and its strategy state), exactly like instance registrations.
- A factory exception is not cached. Concurrent callers observing one failed attempt receive that
  failure; the next resolve retries the factory and caches the first successful result.
- `AddShield` registers the `IKevlarRegistry` for you (`AddKevlar()` exists if you ever need just the registry).

Use [`AddPartitionedShield`](partitioning.md#dependency-injection) when one registration must retain
independent shield state per tenant, endpoint, or other key while remaining bounded.

## Dynamic shields

Use `GetOrAdd` for names discovered after the service provider is built. One factory runs for each
name and result type, even under concurrent first access. Factory failures are not cached, so a
later call can recover. `TryAdd` installs a lazy factory only when the name is free; `Remove`
forgets the registration without disposing a shield already held by a caller.

```csharp
using var dynamicServices = new ServiceCollection().AddKevlar().BuildServiceProvider();
var dynamicRegistry = dynamicServices.GetRequiredService<IKevlarRegistry>();
var tenantShield = dynamicRegistry.GetOrAdd(
    "tenant-north",
    static _ => Shield.Retry(3));

if (!dynamicRegistry.TryAdd("plugin", static _ => Shield.Timeout(TimeSpan.FromSeconds(5))))
{
    throw new InvalidOperationException("The plugin shield was already registered.");
}
```

Dynamic names are registry-only: Microsoft DI's keyed-service table is fixed when the provider is
built. Register with `AddShield` before build when `[FromKeyedServices]` resolution is required.
The registry is thread-safe and is disposed with the service provider. Disposal rejects later
lookups and disposes resolved strategies that implement `IDisposable`/`IAsyncDisposable`.

## Binding shields from configuration

`AddShield(name, IConfiguration)` builds a shield from a configuration section, so retry counts,
timeouts and breaker thresholds are tunable per environment without a redeploy. Use
`AddShield<TResult>(name, configuration)` when consumers need a result-aware shield:

The Generic Host already loads `appsettings.json`. A standalone application can add the same JSON provider explicitly:

```bash
dotnet add package Microsoft.Extensions.Configuration.Json
```

```json
// appsettings.json
{
  "Resilience": {
    "GitHub": {
      "Timeout": "00:00:30",
      "Retry": { "MaxRetries": 5, "Backoff": "Exponential", "MaxDelay": "00:00:10" },
      "CircuitBreaker": { "FailureRatio": 0.5, "BreakDuration": "00:00:15" },
      "AttemptTimeout": "00:00:05"
    }
  }
}
```

```csharp
using Kevlar.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();
services.AddShield("github", configuration.GetSection("Resilience:GitHub"));
```

The schema is `ShieldDefinition`. `ShieldDefinition.Build()` always chains the sections it finds in one fixed order, outermost first:

```text
Timeout → Retry → CircuitBreaker → RateLimit → ConcurrencyLimit → AttemptTimeout
```

Read that the same way as any [fluent chain](composition.md): `Timeout` is the total budget wrapping everything, retries happen inside it, each attempt passes through the breaker and the two limiters, and `AttemptTimeout` is the innermost per-attempt budget. Only the sections you declare are added; the remaining ones keep their relative order. The defaults inside each section match the fluent API's.

Configuration cannot reorder that chain — the order is what makes a definition readable across environments. Build the shield with the fluent API and register the instance when you need a different shape.

### Reloading configuration atomically

`AddShield(name, IConfiguration)` intentionally binds once, when first resolved. Use
`AddReloadingShield` when future configuration reloads must affect new operations:

```csharp
using Kevlar.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();
services.AddReloadingShield(
    "github",
    configuration.GetSection("Resilience:GitHub"),
    error => logger.LogError(error, "Rejected GitHub shield configuration"));
```

Consume the keyed provider and read `Current` once per operation:

<!-- doc-test-ignore: Application client type requires the host's FetchUserAsync implementation. -->
```csharp
public sealed class GitHubClient(
    [FromKeyedServices("github")] IShieldProvider provider)
{
    public Task<User> GetUserAsync(string id, CancellationToken ct)
    {
        Shield snapshot = provider.Current;
        return snapshot.ExecuteAsync(
            ct2 => FetchUserAsync(id, ct2),
            ct).AsTask();
    }
}
```

Registry consumers can call `registry.GetShield("github")` once per operation to obtain the
current snapshot. Reloading names intentionally do not register a keyed `Shield`, because such a
singleton would silently become stale; resolve the keyed `IShieldProvider` instead. Every ordinary
`AddShield` registration exposes both a keyed shield and an `IShieldProvider`, whose `Current`
snapshot remains fixed.

The generic forms are symmetric: `AddReloadingShield<TResult>` publishes through
`IShieldProvider<TResult>` and `registry.GetShield<TResult>(name)`. It likewise omits a keyed
`Shield<TResult>` so consumers cannot accidentally retain a stale snapshot.

Changes are debounced for 250 milliseconds by default, coalescing file-watcher bursts into one
rebuild. Pass a `ReloadingShieldOptions` instance to customize `DebounceDelay` or `TimeProvider`.
On a valid change, Kevlar builds the entire replacement before one atomic publish. Operations
already using the prior snapshot finish on it. Invalid configuration keeps the last known-good
snapshot and invokes the optional failure callback; callback exceptions are contained so later
reloads remain active. Each successful replacement starts with fresh circuit-breaker,
rate-limiter, and concurrency-limiter state. The provider performs no binding, locking, or
allocation while reading `Current`; all rebuild work occurs on configuration change. Disposing
the service provider removes the change-token subscription.

Named options can drive the same atomic replacement model without the fixed `ShieldDefinition`
schema. The options name and shield name are the same; a change for another name is ignored.

<!-- doc-test-declaration -->
```csharp
public sealed class CatalogResilienceOptions
{
    public int MaxRetries { get; set; } = 3;
}
```

```csharp
services.AddReloadingShield<CatalogResilienceOptions>(
    "catalog",
    static (options, _) => Shield.Retry(options.MaxRetries),
    error => Console.Error.WriteLine(error.Message));
```

Resolve `IOptionsMonitor<CatalogResilienceOptions>` through normal Microsoft options registration.
Successful changes publish a fresh shield; build or validation failures retain the prior snapshot
and reach the failure callback. Use `AddReloadingShield<TOptions, TResult>` for typed shields.

## Consuming as a keyed service

Named shields are also registered as keyed services, so you can skip the registry entirely:

<!-- doc-test-declaration -->
```csharp
public sealed class GitHubClient([FromKeyedServices("github")] Shield shield)
{
    public Shield Resilience { get; } = shield;
}
```

## Naming shields

`WithName` stamps a name onto the shield itself, which then shows up as `KevlarContext.ShieldName` in [custom strategies](custom-strategies.md) and callbacks — useful for logging and metrics:

```csharp
Shield.Retry(3).WithName("github");
```

`AddShield("github", …)` registers under that DI name either way; `WithName` is about observability inside the pipeline.

For `HttpClient` pipelines specifically, see the [HTTP integration](http.md) — it builds on this package's registration model.

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

- Shields are keyed by **name + result type** — an untyped `"api"` shield and a `Shield<HttpResponseMessage>` named `"api"` coexist independently.
- Last registration for a given name wins, matching standard DI override behaviour.
- Ordinary factory registrations run lazily on first resolve and the result is cached — so every consumer shares one instance (and its strategy state), exactly like instance registrations.
- `AddShield` registers the `IKevlarRegistry` for you (`AddKevlar()` exists if you ever need just the registry).

Use [`AddPartitionedShield`](partitioning.md#dependency-injection) when one registration must retain
independent shield state per tenant, endpoint, or other key while remaining bounded.

## Binding shields from configuration

`AddShield(name, IConfiguration)` builds a shield from a configuration section, so retry counts, timeouts and breaker thresholds are tunable per environment without a redeploy:

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
var configuration = new ConfigurationBuilder().Build();
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
var configuration = new ConfigurationBuilder().Build();
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
current snapshot. A keyed `Shield` resolved from DI is also one immutable snapshot; it does not
change after a reload. Every ordinary `AddShield` registration exposes an `IShieldProvider` too,
but its `Current` snapshot remains fixed.

On a valid change, Kevlar builds the entire replacement before one atomic publish. Operations
already using the prior snapshot finish on it. Invalid configuration keeps the last known-good
snapshot and invokes the optional failure callback; callback exceptions are contained so later
reloads remain active. Each successful replacement starts with fresh circuit-breaker,
rate-limiter, and concurrency-limiter state. The provider performs no binding, locking, or
allocation while reading `Current`; all rebuild work occurs on configuration change. Disposing
the service provider removes the change-token subscription.

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

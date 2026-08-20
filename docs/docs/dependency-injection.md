---
sidebar_position: 7
---

# Dependency Injection

The `Kevlar.Extensions.DependencyInjection` package registers named policies with `Microsoft.Extensions.DependencyInjection` and exposes them through a registry and as keyed services.

```bash
dotnet add package Kevlar.Extensions.DependencyInjection
```

## Registering policies

```csharp
// A policy instance, under a name:
services.AddKevlarPolicy("github",
    Policy.Timeout(TimeSpan.FromSeconds(10)).Retry(3));

// A typed policy, built from the service provider (loggers, config, etc.):
services.AddKevlarPolicy<HttpResponseMessage>("downstream",
    sp => HttpKevlar.HandleTransient().Retry(3).WithName("downstream"));
```

Because policies are immutable and thread-safe, each named policy is a singleton — which is exactly what you want: every consumer of `"github"` shares the same instance, and therefore the same circuit breaker state, rate-limit bucket and bulkhead slots.

## Consuming via the registry

```csharp
public sealed class GitHubClient(IKevlarRegistry registry)
{
    private readonly Policy _policy = registry.GetPolicy("github");

    public Task<User> GetUserAsync(string id, CancellationToken ct) =>
        _policy.ExecuteAsync(ct2 => FetchUserAsync(id, ct2), ct).AsTask();
}
```

Typed policies come back through `GetPolicy<T>(name)`:

```csharp
var httpPolicy = registry.GetPolicy<HttpResponseMessage>("downstream");
```

`GetPolicy` throws a `KeyNotFoundException` (with an actionable message) for unknown names; `TryGetPolicy(name, out var policy)` / `TryGetPolicy<T>(...)` are the non-throwing forms.

Registry semantics worth knowing:

- Policies are keyed by **name + result type** — an untyped `"api"` policy and a `Policy<HttpResponseMessage>` named `"api"` coexist independently.
- Last registration for a given name wins, matching standard DI override behaviour.
- Factory registrations run lazily on first resolve and the result is cached — so every consumer shares one instance (and its strategy state), exactly like instance registrations.
- `AddKevlarPolicy` registers the `IKevlarRegistry` for you (`AddKevlar()` exists if you ever need just the registry).

## Consuming as a keyed service

Named policies are also registered as keyed services, so you can skip the registry entirely:

```csharp
public sealed class GitHubClient([FromKeyedServices("github")] Policy policy)
{
    // ...
}
```

## Naming policies

`WithName` stamps a name onto the policy itself, which then shows up as `KevlarContext.PolicyName` in [custom strategies](custom-strategies.md) and callbacks — useful for logging and metrics:

```csharp
Policy.Retry(3).WithName("github");
```

`AddKevlarPolicy("github", …)` registers under that DI name either way; `WithName` is about observability inside the pipeline.

For `HttpClient` pipelines specifically, see the [HTTP integration](http.md) — it builds on this package's registration model.

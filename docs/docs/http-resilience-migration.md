---
---

# Migrating HTTP resilience

Moving from `Microsoft.Extensions.Http.Resilience`? Replace `AddStandardResilienceHandler`,
`AddStandardHedgingHandler`, or `AddResilienceHandler`
with the corresponding Kevlar HTTP registration. For named gRPC resilience, use
[gRPC interceptors](grpc.md): `AddShieldUnaryInterceptor` and `AddShieldStreamingInterceptor`.
For server admission control, register `AddShieldServerInterceptor` and add `ShieldServerInterceptor`
to the ASP.NET Core gRPC interceptor pipeline. Server shields must guarantee at most one handler
invocation; keep retries and hedging on replay-safe client operations.
Start by comparing defaults and replay rules;
renaming the registration alone changes behavior. For direct Polly pipelines, see the
[Polly migration guide](polly-migration.md).

The Microsoft column below describes `Microsoft.Extensions.Http.Resilience` 10.10.0 with Polly
8.8.0, the versions used by this repository's documentation tests. The Kevlar column describes
this repository's standard registrations. The source links pin the compared versions.

## Registrations

Install `Kevlar.Extensions.Http`. HTTP registration extensions live in
`Microsoft.Extensions.DependencyInjection`; HTTP options and runtime helpers live in
`Kevlar.Extensions.Http`.

<div style={{overflowX: 'auto'}}>

| Microsoft registration | Kevlar registration |
| --- | --- |
| `AddStandardResilienceHandler()` | `AddStandardShield()` |
| `AddStandardHedgingHandler()` | `AddStandardHedgeShield()` |
| `AddResilienceHandler(name, builder => ...)` | Build a `Shield<HttpResponseMessage>`, then `AddShield(shield)` or use the service-provider factory overload |
| `AddResilienceHandler(name, (builder, context) => ...)` | Use the service-provider factory for setup; use `AddShield((request, serviceProvider) => shield)` for request selection |
| `RemoveAllResilienceHandlers()` | Remove the Microsoft handlers during migration; `RemoveAllShields()` removes Kevlar handlers only |
| `SetResilienceContext` and context properties | `WithKevlarProperties` and `KevlarHttp.GetRequestOptions(request)` |
| `HttpClientResiliencePredicates.IsTransient` | `HttpShield.IsTransient` |
| `AddPolicyHandlerFromRegistry(name)` | Register a named `Shield<HttpResponseMessage>`, then `AddShield(name)` |

</div>

Before:

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpClient("catalog")
    .AddStandardResilienceHandler(options => options.Retry.MaxRetryAttempts = 3);
```

After:

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpClient("catalog")
    .AddStandardShield(options => options.Retry.MaxRetries = 3);
```

Remove the old registration when adding the new one. Stacking both handlers multiplies attempts
and gives the same request multiple timeout and breaker scopes.

## Standard retry defaults

`HttpStandardResilienceOptions` maps to `StandardHttpShieldOptions`:

<div style={{overflowX: 'auto'}}>

| Microsoft field and default | Kevlar field and default |
| --- | --- |
| `RateLimiter.DefaultRateLimiterOptions.PermitLimit = 1000`, `QueueLimit = 0` | `ConcurrencyLimit = null`: no limiter. To enable it, set `MaxConcurrency` and `QueueLimit`; a new `ConcurrencyLimitOptions` defaults to 10 and 0 |
| `TotalRequestTimeout.Timeout = 30s` | `TotalTimeout.Timeout = 30s` |
| `Retry.MaxRetryAttempts = 3` | `Retry.MaxRetries = 3`; both allow four total attempts |
| `Retry.Delay = 2s`, `BackoffType = Exponential`, `UseJitter = true` | `Retry.Backoff`: exponential from 250ms, factor 2, equal jitter |
| `Retry.MaxDelay = null` | `Retry.MaxDelay = 10s` in the standard HTTP pipeline |
| `Retry.ShouldRetryAfterHeader = true` | `UseRetryAfterHeader = true` |
| `CircuitBreaker.FailureRatio = 0.1` | `CircuitBreaker.FailureRatio = 0.5` |
| `CircuitBreaker.MinimumThroughput = 100` | `CircuitBreaker.MinimumThroughput = 10` |
| `CircuitBreaker.SamplingDuration = 30s` | `CircuitBreaker.SamplingWindow = 30s` |
| `CircuitBreaker.BreakDuration = 5s` | `CircuitBreaker.BreakDuration = 15s` |
| `AttemptTimeout.Timeout = 10s` | `AttemptTimeout.Timeout = 10s` |

</div>

These defaults come from the [Microsoft standard options](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/HttpStandardResilienceOptions.cs),
[HTTP retry options](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpRetryStrategyOptions.cs),
and Polly's [retry](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Retry/RetryStrategyOptions.TResult.cs),
[breaker](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/CircuitBreaker/CircuitBreakerStrategyOptions.TResult.cs),
and [limiter](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.RateLimiting/RateLimiterStrategyOptions.cs) options.

The pipeline order also differs, outermost first:

- Microsoft: concurrency limiter, total timeout, retry, breaker, attempt timeout.
- Kevlar: total timeout, retry, breaker, optional concurrency limiter, attempt timeout.

Microsoft's outer limiter holds a permit across retries and their delays. Kevlar's standard
limiter limits each attempt, with the wait covered by the total timeout. Copying permit and queue
values does not copy that scope. Use a [custom fluent pipeline](http.md#behaviour-notes) when the
limiter must enclose the whole operation.

Both standard registrations disable `HttpClient.Timeout` so their timeout strategies own the
budgets. With Kevlar, configure client timeout before `AddStandardShield`, and tune
`TotalTimeout` and `AttemptTimeout` on the shield. A custom `AddShield` registration does not
apply the standard timeout override.

Both HTTP integrations handle transient HTTP failures, including 408, 429, and 5xx responses.
Translate `ShouldHandle` deliberately: Kevlar's `HandlesException`/`HandlesResult` replace the
ambient clause for that strategy. Retry jitter distributions are not identical. Microsoft's
HTTP `Retry-After` generator supplies a delay in place of the backoff. Without a custom generator,
Kevlar uses the header only to lengthen its backoff. With a custom generator, it combines the
generator and header suggestions using the longer value, then caps the final delay. See [HTTP integration](http.md)
for the full handling and delay contracts.

## Standard hedging defaults

`HttpStandardHedgingResilienceOptions` maps to `StandardHedgeShieldOptions`:

<div style={{overflowX: 'auto'}}>

| Microsoft field and default | Kevlar field and default |
| --- | --- |
| `TotalRequestTimeout.Timeout = 30s` | `TotalTimeout.Timeout = 30s` |
| `Hedging.MaxHedgedAttempts = 1`, `Delay = 2s` | `Hedge.MaxHedgedAttempts = 1`, `Delay = 1s`; both counts mean additional attempts |
| `Endpoint.RateLimiter`: 1000 permits, queue 0 | `ConcurrencyLimit = null`; set an explicit limiter to enable it |
| `Endpoint.CircuitBreaker`: 10%, minimum 100, 30s sample, 5s break | `CircuitBreaker`: 50%, minimum 10, 30s sample, 15s break |
| `Endpoint.Timeout.Timeout = 10s` | `AttemptTimeout.Timeout = 10s` |

</div>

The Microsoft hedging attempt default is **one additional attempt**, not ten; ten is the upper
configuration limit in the compared [Polly hedging options](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Hedging/HedgingStrategyOptions.TResult.cs).
Microsoft's [endpoint options](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.Http.Resilience/Hedging/HedgingEndpointOptions.cs)
are separate from its total timeout and hedging options.

Both standard hedging pipelines enclose authority-local protection with total timeout and hedging.
For Kevlar, the endpoint order is optional concurrency limiter, breaker, attempt timeout. Without
routing configuration, attempts use the request's original authority. Breaker and limiter state
are isolated by authority; custom Microsoft `SelectPipelineBy(...)` keys have no automatic mapping
to Kevlar's standard authority cache. Use a [request-keyed partition](http.md#per-request-options)
when application-specific isolation is required.

## Ordered and weighted routes

Microsoft's `IRoutingStrategyBuilder.ConfigureOrderedGroups` visits groups in order and selects
one endpoint by weight inside each group. `ConfigureWeightedGroups` also weights group selection;
`WeightedGroupSelectionMode.InitialAttempt` chooses the first group by weight, then visits the
remaining groups in order. `EveryAttempt` weights each remaining group selection. These are
[two-level routing policies](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.Http.Resilience/Routing/Internal/WeightedGroups/WeightedGroupsRoutingStrategy.cs).

Kevlar exposes a flat `HttpEndpointRoutingOptions.Endpoints` list:

<div style={{overflowX: 'auto'}}>

| Existing route policy | Kevlar migration |
| --- | --- |
| Ordered groups, one endpoint per group | `HttpEndpointSelectionMode.Ordered` with endpoints in group order |
| Weighted groups, one endpoint per group, `EveryAttempt` | `HttpEndpointSelectionMode.Weighted` with group weights as endpoint weights |
| Several weighted endpoints in each group | No direct group equivalent; preserve group policy in application routing or a complete `RequestFactory` |
| `InitialAttempt` weighted, then configured group order | No exact built-in equivalent; `Weighted` weights the whole endpoint order |

</div>

Ordered routing example:

```csharp
using System;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpClient("catalog")
    .AddStandardHedgeShield(options =>
    {
        options.Hedge.MaxHedgedAttempts = 1;
        options.Hedge.Delay = TimeSpan.FromSeconds(2);
        options.Routing = new HttpEndpointRoutingOptions
        {
            SelectionMode = HttpEndpointSelectionMode.Ordered,
        };
        options.Routing.Endpoints.Add(new HttpEndpoint(new Uri("https://primary.example")));
        options.Routing.Endpoints.Add(new HttpEndpoint(new Uri("https://backup.example")));
    });
```

For weighted ordering, set `SelectionMode = HttpEndpointSelectionMode.Weighted` and construct
endpoints with `weight: 3`, `weight: 1`, and so on. `Seed` makes the sequence reproducible for
tests; it is not a traffic percentage guarantee. Kevlar visits every endpoint before cycling,
whereas Microsoft's configured groups are exhausted. Match `MaxHedgedAttempts` to the intended
attempt budget instead of relying on group exhaustion.

Both integrations route by authority while retaining the request path and query. Endpoint URI
paths are not a way to rewrite an application route. If different attempts need different paths,
use application routing or a `RequestFactory` that builds the intended full URI. Do not flatten
weighted groups when group boundaries carry business meaning. See
[standard hedging](http.md#standard-hedging) for replay and ownership rules.

## Context, dependency injection, and reload

`ResilienceHandlerContext` is a **pipeline-construction context**, not a request context.
Its `ServiceProvider` supports dependency-aware construction; `BuilderName` and `InstanceName`
identify the Microsoft pipeline. The handler name passed to `AddResilienceHandler` does not
become a Kevlar registry entry automatically. Register named shields explicitly when needed.

For build-time dependencies, use Kevlar's `AddShield(serviceProvider => shield)` overload. For
per-request selection, return an existing shared shield from the request-aware overload:

```csharp
using System.Net.Http;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddShield<HttpResponseMessage>("reads", HttpShield.Standard());
services.AddShield<HttpResponseMessage>("writes", HttpShield.WhenTransient()
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(15)));
services.AddHttpClient("catalog")
    .AddShield((request, serviceProvider) =>
        serviceProvider.GetRequiredService<IKevlarRegistry>()
            .GetShield<HttpResponseMessage>(request.Method == HttpMethod.Get ? "reads" : "writes"));
```

Do not build a new breaker or limiter inside that selector: its state would restart for each
request. The selector's service provider is not the ASP.NET request scope; use request options
to pass request-specific data.

Microsoft's [`EnableReloads<TOptions>` and `GetOptions<TOptions>`](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.Http.Resilience/Resilience/ResilienceHandlerContext.cs)
provide construction-time options and reload subscriptions. A Microsoft registration can use them
as follows:

```csharp
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

var services = new ServiceCollection();
services.Configure<HttpRetryStrategyOptions>("catalog", options => options.MaxRetryAttempts = 2);
services.AddHttpClient("catalog")
    .AddResilienceHandler("outbound", (builder, context) =>
    {
        context.EnableReloads<HttpRetryStrategyOptions>("catalog");
        builder.AddRetry(context.GetOptions<HttpRetryStrategyOptions>("catalog"));
    });
```

Use Kevlar's configuration-backed standard registration, or named `AddReloadingShield<TOptions, TResult>`
registration followed by `AddShield(name)`. For example:

```csharp
using System.Net.Http;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.Configure<RetryDefinition>("catalog", options => options.MaxRetries = 2);
services.AddReloadingShield<RetryDefinition, HttpResponseMessage>(
    "catalog",
    static (options, _) => HttpShield.WhenTransient().Retry(options.MaxRetries));
services.AddHttpClient("catalog").AddShield("catalog");
```

The named options monitor supplies reload notifications when the application's options source
changes. The HTTP registration resolves the current shield for each request; invalid reloads keep
the last valid publication. See [reload behavior](dependency-injection.md#reloading-configuration-atomically).
Request options also expose `WithShield`, `WithShieldName`, and `WithKevlarCancellationToken`
for overrides and cancellation linking; see [per-request options](http.md#per-request-options).

Microsoft's `OnPipelineDisposed` callback has no direct registration-context equivalent; follow
Kevlar's [strategy ownership](library-authors.md) contract for disposable resources.

## Request snapshots and replay safety

Microsoft's internal [`RequestMessageSnapshot`](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.Http.Resilience/Internal/RequestMessageSnapshot.cs)
copies method, URI, version, headers, and request properties/options for hedging. It shares the
content object and rejects `StreamContent`; it is not a bounded body-buffering policy.

Kevlar separates reconstruction from permission to repeat the operation:

<div style={{overflowX: 'auto'}}>

| Concern | Kevlar behavior |
| --- | --- |
| Re-readable content | `HttpContentReplayPolicy.NoBuffer` by default; built-in byte/string/form and ordinary JSON content can be reused |
| One-shot body | Remains single-attempt with `NoBuffer`; select `Buffer` or supply a `RequestFactory` to replay it |
| Explicit buffering | `Buffer` serializes once before transport; `MaxBufferSize` defaults to 1 MiB; overflow or serialization failure raises `HttpRequestReplayException` before sending |
| Complete custom replay | `RequestFactory` builds each complete request; the handler owns factory-created messages |
| Additional attempts | GET, HEAD, OPTIONS, TRACE, PUT, and DELETE are replayable by default; POST, PATCH, and custom methods require opt-in |

</div>

Microsoft's standard retry handler does not disable unsafe methods by default. Its
[`DisableForUnsafeHttpMethods`](https://github.com/dotnet/extensions/blob/v10.10.0/src/Libraries/Microsoft.Extensions.Http.Resilience/Polly/HttpRetryStrategyOptionsExtensions.cs)
excludes POST, PATCH, PUT, DELETE, and CONNECT. That set differs from Kevlar's default **idempotent**
method set: PUT and DELETE remain replayable in Kevlar. Use `request.DisableReplay()` when those
operations must stay single-attempt.

For a known-idempotent POST, use `request.AllowReplay()`, or opt in for the client with
`Handler.AllowUnsafeMethodReplay`. A `RequestFactory` also counts as explicit opt-in, but none of
these settings make a non-idempotent server operation safe. Permission still requires replayable
content. Prefer a server-supported idempotency key. See [safe request replay](http.md#safe-request-replay)
for examples, clone ownership, and suppression telemetry.

## Global defaults and handler lifetime

Replace the Microsoft default registration at the composition root:

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.ConfigureHttpClientDefaults(client => client.AddStandardShield());
services.AddHttpClient("catalog");
services.AddHttpClient("health").RemoveAllShields();
```

If Microsoft handlers are still registered through another defaults callback, remove them with
`RemoveAllResilienceHandlers()` while that package remains referenced. Neither removal API removes
the other library's handlers. Apply per-client overrides after global defaults and avoid adding a
second standard shield on top of the inherited one.

`IHttpClientFactory` rotates handler instances. Kevlar's standard registrations and service-provider
factories cache strategy state per named client registration across those rotations. Shortening
`SetHandlerLifetime` does **not** reset a breaker, limiter, or endpoint cache. Configuration reload
publishes a new snapshot; executions already in flight keep the old one. If state should intentionally
restart for each handler lifetime, construct `ShieldDelegatingHandler` in `AddHttpMessageHandler`
instead. Fixed-shield overloads share the supplied instance; request selectors share whatever
instances they return. See [HTTP lifetime notes](http.md#behaviour-notes).

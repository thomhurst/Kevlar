---
sidebar_position: 8
---

# HTTP Integration

The `Kevlar.Extensions.Http` package plugs shields into `HttpClientFactory` as a `DelegatingHandler`, with transient-fault handling and `Retry-After` support built in.

```bash
dotnet add package Kevlar.Extensions.Http
```

## The one-liner

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("api")
    .AddStandardShield();
```

`AddStandardShield` wires up the pipeline you'd have built anyway (outermost first):

HTTP registration extensions live in `Microsoft.Extensions.DependencyInjection`, which ASP.NET
Core projects import implicitly. Import `Kevlar.Extensions.Http` only when using HTTP options or
runtime types such as `HttpShield`.

1. **30s total timeout** around everything
2. **3 jittered retries** (exponential from 250ms, capped 10s) — honouring `Retry-After` headers and disposing superseded responses
3. **Circuit breaker** — sampling mode: opens at a 50% failure ratio over a 30s window (minimum 10 calls), breaks for 15s
4. **10s attempt timeout** per individual try

The standard registration disables `HttpClient.Timeout`; its attempt and total timeout strategies
own timeout behavior inside the retry boundary. Configure `AttemptTimeout` and `TotalTimeout`
instead. If you configure `HttpClient.Timeout` on the same builder, call `AddStandardShield` after
that configuration.

Customize those stages without rebuilding the pipeline:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("api")
    .AddStandardShield(options =>
    {
        options.TotalTimeout.Timeout = TimeSpan.FromSeconds(20);
        options.Retry.MaxRetries = 2;
        options.CircuitBreaker.FailureRatio = 0.25;
        options.CircuitBreaker.HandlesResult = response =>
            response.StatusCode == HttpStatusCode.ServiceUnavailable;
        options.ConcurrencyLimit = new ConcurrencyLimitOptions
        {
            MaxConcurrency = 100,
            QueueLimit = 20,
        };
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.Handler.ContentReplayPolicy = HttpContentReplayPolicy.Buffer;
        options.Handler.MaxBufferSize = 256 * 1024;
    });
```

`StandardHttpShieldOptions` exposes the total timeout, typed retry and circuit-breaker options,
optional concurrency limiter, attempt timeout, and handler replay/routing options. Replacing
`Retry` still honours `Retry-After` by default; set `UseRetryAfterHeader = false` to opt out. Set
either timeout's `Timeout` to `Timeout.InfiniteTimeSpan` to omit that stage. A finite attempt timeout
cannot exceed a finite total timeout. Invalid strategy values fail while the registration is built;
handler replay/routing values fail when
`HttpClientFactory` builds its handler pipeline, before a request is sent.

The breaker is a `CircuitBreakerOptions<HttpResponseMessage>`, so `HandlesResult` can replace the
standard transient-result clause for that stage without changing retry handling.

For dependency-aware setup, use the service-provider overload:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

services.AddSingleton(new ConcurrencyLimitOptions
{
    MaxConcurrency = 100,
    QueueLimit = 20,
});
services.AddHttpClient("api")
    .AddStandardShield((serviceProvider, options) =>
    {
        options.ConcurrencyLimit =
            serviceProvider.GetRequiredService<ConcurrencyLimitOptions>();
    });
```

The one-argument callback runs during registration. The service-provider callback runs lazily on
first client creation using the application service provider. Both build one shield for the named
client registration, so breaker, limiter, and other strategy state survives handler rotation.

## Configuration and reload

Pass an `IConfiguration` section to bind the standard pipeline and reload it when the section's
change token fires:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Http:Api:TotalTimeout"] = "00:00:20",
        ["Http:Api:Retry:MaxRetries"] = "2",
        ["Http:Api:Retry:Backoff"] = "Exponential",
        ["Http:Api:Retry:BaseDelay"] = "00:00:00.100",
        ["Http:Api:Retry:Jitter"] = "Equal",
        ["Http:Api:AttemptTimeout"] = "00:00:05",
        ["Http:Api:Handler:MaxBufferSize"] = "262144",
    })
    .Build();

services.AddHttpClient("api")
    .AddStandardShield(
        configuration.GetSection("Http:Api"),
        onReloadFailure: exception =>
            Console.Error.WriteLine(exception.Message));
```

Timeouts accept either a scalar (`TotalTimeout`) or the options-shaped
`TotalTimeout:Timeout` key. Retry keys are `MaxRetries`, `Backoff` (`None`, `Constant`, `Linear`,
or `Exponential`), `BaseDelay`, `Factor`, `Jitter` (`None`, `Equal`, `Full`, or `Decorrelated`),
`BackoffMaxDelay`, and `MaxDelay`; `UseRetryAfterHeader` is a root standard-shield key. Circuit
breaker, concurrency-limit, handler, routing, and endpoint keys match their public option-property
names. Endpoint entries accept either a URI scalar or `Uri` plus optional `Weight` children.

Configuration is applied first. The service-provider callback overload runs afterward, so DI values
and delegates can deliberately override bound values:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Retry:MaxRetries"] = "2",
    })
    .Build();

services.AddLogging();
services.AddHttpClient("api")
    .AddStandardShield(configuration, (serviceProvider, options) =>
    {
        var logger = serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("HttpResilience");
        options.Retry.OnRetry = retry =>
        {
            logger.LogWarning("Retry {AttemptNumber}", retry.AttemptNumber);
            return default;
        };
    });
```

Each request captures one immutable shield-and-handler snapshot. A valid reload builds the whole
replacement before publishing it atomically; in-flight requests finish on their original snapshot.
Invalid binding or validation keeps the last valid snapshot and calls `onReloadFailure` with the
full configuration path. A successful reload starts fresh breaker, limiter, and endpoint-local
state. `HttpClientFactory` handler rotation reuses the current snapshot and does not rerun the
service-provider callback. The application service provider owns the reload subscription.

Hedging uses the same reload contract. Its keys follow the nested
`StandardHedgeShieldOptions` shape. `Routing` is optional; without it, attempts keep the request's
authority:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Hedge:MaxHedgedAttempts"] = "1",
        ["Hedge:Delay"] = "00:00:00.500",
        ["Routing:SelectionMode"] = "Weighted",
        ["Routing:Endpoints:0:Uri"] = "https://api-a.example",
        ["Routing:Endpoints:0:Weight"] = "3",
        ["Routing:Endpoints:1:Uri"] = "https://api-b.example",
    })
    .Build();

services.AddHttpClient("routed")
    .AddStandardHedgeShield(configuration);
```

## Bring your own pipeline

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("api")
    .AddShield(
        HttpShield.WhenTransient()
            .Retry(o =>
            {
                o.MaxRetries = 4;
                o.DelayGenerator = HttpShield.RetryAfter;
            })
            .CircuitBreaker(o => o.FailureRatio = 0.5),
        new ShieldHttpHandlerOptions
        {
            ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
            MaxBufferSize = 1024 * 1024,
        });
```

You can also grab that exact shield directly with `HttpShield.Standard()`.

### `HttpShield.WhenTransient()`

Starts a typed `Shield<HttpResponseMessage>` builder with the standard transient-fault handling clause:

- `HttpRequestException`
- `HttpClient.Timeout` (`TaskCanceledException` with an inner `TimeoutException`)
- attempt timeouts (`TimeoutExceededException`)
- HTTP 500–599 responses (numeric status codes outside that range are not treated as 5xx)
- HTTP 408 (Request Timeout)
- HTTP 429 (Too Many Requests)

Use `HttpShield.IsTransient(response)` for the status-code test alone. Use
`HttpShield.IsTransientException(exception, callerCancellationToken)` when classifying an exception
outside a shield; caller cancellation is never transient. On the `netstandard2.0` asset, a bare
`TaskCanceledException` with no cancellable token is treated as the legacy `HttpClient.Timeout`
shape.

### `HttpShield.RetryAfter`

A `DelayGenerator` for retry options: when the failed response carries a `Retry-After` header
(delta or date form), the retry waits what the server asked for, capped at 30 seconds by default.
The server's suggestion is used only when it's *longer* than the computed backoff; no header →
normal backoff applies. It returns a completed `ValueTask<TimeSpan?>`, so it binds directly as a
method group (`options.DelayGenerator = HttpShield.RetryAfter`) and works with synchronous
`Execute`. Use `HttpShield.RetryAfter(maxDelay)` to choose another cap.

The standard shield composes this with a custom `Retry.DelayGenerator` and uses the longer result,
awaiting the custom generator when it yields. Set `UseRetryAfterHeader` to `false` when the custom
generator must have exclusive control.

The standard shield caps every retry delay at 10 seconds, so one excessive server suggestion cannot
impose an unbounded wait. Custom shields can cap server-suggested delays directly:

```csharp
using Kevlar.Extensions.Http;

var shield = HttpShield.WhenTransient()
    .Retry(options =>
    {
        options.DelayGenerator = HttpShield.RetryAfter(TimeSpan.FromSeconds(5));
    });
```

### Registering a shield built elsewhere

```csharp
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("api")
    .AddShield("downstream");
```

The named overload resolves `IKevlarRegistry.GetShield<HttpResponseMessage>` for every request, so
reload-aware registrations and dynamic registry replacements are observed without rebuilding the
`HttpClient` handler pipeline. A missing name throws `KeyNotFoundException` on the first request.

`AddShield` also accepts a shield instance, an `IServiceProvider` factory, or a per-request selector.
Call `RemoveAllShields()` after the relevant registrations to remove Kevlar handlers from that named
client while leaving unrelated delegating handlers intact. It also removes the
`HttpClient.Timeout = Timeout.InfiniteTimeSpan` overrides installed by preceding standard shields,
so an earlier custom client timeout (or the normal default) remains effective.

## Per-request options

Attach execution properties, select a shield, allow or suppress replay, or link another
cancellation token without changing the named client's defaults:

```csharp
using Kevlar.Extensions.Http;

var tenantKey = new KevlarKey<string>("tenant");
using var request = new HttpRequestMessage(HttpMethod.Post, "orders")
    .WithKevlarProperties(properties => properties.Set(tenantKey, "north"))
    .WithShieldName("orders-write")
    .DisableReplay()
    .WithKevlarCancellationToken(cancellationToken);

using var response = await httpClient.SendAsync(request, cancellationToken);
```

The property initializer runs once for the outer request execution and once for each separately
executed endpoint-local shield. Retries reuse their context, while hedges copy the initialized
properties into forked contexts instead of rerunning the initializer. Pooled properties are cleared
after execution, so values do not leak to later requests. `DisableReplay` and `AllowReplay` affect
only this request, and the last call wins. `DisableReplay` keeps any request, including a GET,
single-attempt. `AllowReplay` lets one known-idempotent POST, PATCH, or custom-method request retry
or hedge; content must still satisfy the normal replay-safety rules. See
[method safety](#method-safety) for the reasoning and the idempotency-key pattern.

Choose one shield per request with the selector overload. Selection happens once, before the
shield executes. A direct `.WithShield(shield)` request override takes precedence over the
selector; `ShieldName` is metadata for selectors and has no global lookup behavior:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var readShield = HttpShield.WhenTransient().Retry(3);
var writeShield = HttpShield.WhenTransient().Retry(0);

services.AddHttpClient("api")
    .AddShield((request, serviceProvider) =>
        KevlarHttp.GetRequestOptions(request).ShieldName == "orders-write"
            ? writeShield
            : readShield);
```

For isolated, bounded state per request key, connect a [`PartitionedShield`](partitioning.md)
directly:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var tenantShields = new PartitionedShield<string, HttpResponseMessage>(
    _ => HttpShield.WhenTransient()
        .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30)));

services.AddHttpClient("tenant-api")
    .AddShield(
        tenantShields,
        request => request.Headers.GetValues("X-Tenant").Single());
```

On .NET 8 and later, `KevlarHttp.RequestOptions` is the public typed
`HttpRequestOptionsKey<KevlarRequestOptions>` for direct `HttpRequestMessage.Options` access.
`GetRequestOptions` and the request extensions also work through `HttpRequestMessage.Properties`
when consuming the `netstandard2.0` asset. Built-in replay clones carry the same request-options
object. A custom `RequestFactory` creates the complete request and must copy any desired options.

## Safe request replay

Every retry and hedge needs a fresh `HttpRequestMessage`: .NET sends a message once, and one-shot
content is consumed by the first send. Kevlar rebuilds the message for you. Two independent checks
then decide whether an additional attempt may be sent:

1. **Can the message be rebuilt?** A mechanical question about the content. Kevlar answers it
   automatically for most requests.
2. **Is it safe to send twice?** A semantic question about the operation. Only the caller can
   answer it for POST, PATCH, and custom methods.

When method replay is suppressed, or `NoBuffer` rejects non-replayable content, the shield stays
single-attempt for that request instead of failing it. With `Buffer`, content that exceeds
`MaxBufferSize` or fails serialization throws `HttpRequestReplayException` before the first
transport attempt.

### Rebuilding the message

The first no-routing attempt sends the caller's original request directly. Additional attempts use
clones that preserve method, URI, HTTP version and version policy, request headers, request options,
and content headers. The handler owns every clone and every nonselected response; the caller owns
the original request and the returned response.

Content replay depends on `ContentReplayPolicy`:

- `NoBuffer` (default) reuses inherently re-readable content such as `ByteArrayContent`,
  `StringContent`, `FormUrlEncodedContent`, and ordinary `JsonContent`. Positive-length content
  already loaded into its HTTP buffer is also reusable. `JsonContent` declared as
  `IAsyncEnumerable<T>` and one-shot content such as `StreamContent` are sent once; call
  `LoadIntoBufferAsync()` first, select `Buffer`, or provide a `RequestFactory` to replay them.
- `Buffer` serializes content once before sending, bounded by `MaxBufferSize`, then gives each
  attempt its own `ByteArrayContent`. Oversize or partial serialization fails before attempt 1.
- `RequestFactory` creates a complete fresh request per attempt. Use it for one-shot streams,
  generated bodies, signatures, or other request state that cannot be cloned. Factory requests are
  disposed by the handler.

### Method safety

GET, HEAD, OPTIONS, TRACE, PUT, and DELETE are idempotent by definition
([RFC 9110 §9.2.2](https://www.rfc-editor.org/rfc/rfc9110#section-9.2.2)), so Kevlar replays them
automatically. POST, PATCH, and custom methods are not, and a perfect clone does not change that:
the risk is the server executing the operation twice, not the client rebuilding the message. A
hedged `POST /orders` is two concurrent order creations unless the server deduplicates them, and
Kevlar cannot know whether it does. These methods therefore stay single-attempt until you opt in:

| Opt-in | Scope | Use when |
| --- | --- | --- |
| `request.AllowReplay()` | one request | This request is idempotent, typically because it carries an idempotency key. |
| `Handler.AllowUnsafeMethodReplay = true` | every request on the client | The API deduplicates every write, or the client only sends idempotent unsafe-method requests. |
| `Handler.RequestFactory` | every request on the client | Each attempt needs a freshly built request (streams, signatures, generated bodies). Building every attempt yourself counts as opt-in. |

Opting in does not skip the content check: a POST with one-shot content still needs `Buffer`,
`LoadIntoBufferAsync()`, or a `RequestFactory`. `request.DisableReplay()` forces any request,
including a GET, to stay single-attempt.

The recommended pattern for a retried or hedged write is an idempotency key that the server
deduplicates. Clones preserve headers, so every attempt carries the same key, and a duplicate
delivery becomes a harmless repeat rather than a second order:

```csharp
using System.Net.Http.Json;
using Kevlar.Extensions.Http;

using var createOrder = new HttpRequestMessage(HttpMethod.Post, "orders")
{
    Content = JsonContent.Create(new { Sku = "sku-1", Quantity = 2 }),
}.AllowReplay();
createOrder.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

using var created = await httpClient.SendAsync(createOrder, cancellationToken);
```

### Suppressed attempts

If method or content cannot be replayed safely, retry and hedging remain single-attempt: the
original response is returned or the original exception is rethrown without a retry delay or
callback. Other stages, including timeout, circuit breaker, and concurrency limiting, still observe
that attempt. A multi-attempt shield reports this decision once as the `attempts_suppressed`
telemetry event and log 1009, with reason `replay_disabled`, `unsafe_method`, or
`non_replayable_content`. The first `unsafe_method` suppression for a client is a Warning that
names the handler-wide and per-request opt-ins; later unsafe-method suppressions and other reasons
are Information. Telemetry uses the matching Warning or Information severity. The
`kevlar.http.replay_suppressed` counter carries the same bounded reason in
`kevlar.suppression.reason`. `HttpRequestReplayException` is reserved for configuration failures
such as a null factory result or content exceeding the requested buffer limit. Timeouts and caller
cancellation flow to every attempt and request factory.

## Standard hedging

Hedge against the request's own authority without registration-time routing configuration:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("hedged")
    .AddStandardHedgeShield();
```

The request path, query, and authority are preserved for every attempt. To route attempt 1,
attempt 2, and so on across alternate authorities instead, configure endpoints explicitly:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

services.AddHttpClient("routed")
    .AddStandardHedgeShield(options =>
    {
        options.Routing = new HttpEndpointRoutingOptions
        {
            SelectionMode = HttpEndpointSelectionMode.Weighted,
        };
        options.Routing.Endpoints.Add(new HttpEndpoint(new Uri("https://api-a.example"), weight: 3));
        options.Routing.Endpoints.Add(new HttpEndpoint(new Uri("https://api-b.example"), weight: 1));
        options.Hedge.MaxHedgedAttempts = 1;
        options.Hedge.Delay = TimeSpan.FromMilliseconds(500);
        options.Hedge.DelayGenerator = hedge => new(hedge.Elapsed < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.Zero);
    });
```

`AddStandardHedgeShield` installs a 30s total timeout and one additional hedged attempt (two total).
Each authority gets its own 50%-over-30s circuit breaker (minimum 10 attempts, 15s break) and 10s
attempt timeout. No concurrency limiter is installed by default. Set `ConcurrencyLimit` to a new
`ConcurrencyLimitOptions` instance to add an authority-local limiter; configure the remaining
defaults through `TotalTimeout.Timeout`, `Hedge`, `CircuitBreaker`, and `AttemptTimeout.Timeout`.

Request replay is configured through `Handler` (`ContentReplayPolicy`, `MaxBufferSize`,
`AllowUnsafeMethodReplay`, and `RequestFactory`); alternate endpoint authorities and ordering are
configured through `Routing`. An empty endpoint list uses the request's authority. POST, PATCH, and
custom methods still require the same explicit [method-safety](#method-safety) opt-in, handler-wide
or per request with `AllowReplay()`; registering the standard hedging pipeline does not make an
unsafe operation safe to repeat.

For a fully custom endpoint-aware pipeline, compose the outer and endpoint shields directly:

```csharp
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var routing = new HttpEndpointRoutingOptions
{
    SelectionMode = HttpEndpointSelectionMode.Ordered,
    ShieldFactory = endpoint => HttpShield.WhenTransient()
        .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30)),
};
routing.Endpoints.Add(new HttpEndpoint(new Uri("https://api-a.example")));
routing.Endpoints.Add(new HttpEndpoint(new Uri("https://api-b.example")));

services.AddHttpClient("routed")
    .AddShield(
        HttpShield.WhenTransient().Hedge(1, delay: TimeSpan.Zero),
        new ShieldHttpHandlerOptions { Routing = routing });
```

`Ordered` is deterministic configuration order. `Weighted` creates a deterministic weighted
permutation from `Seed` when provided, or from a random initial seed otherwise; a request visits
every configured endpoint before cycling. `ShieldFactory`
is cached by authority, so circuit-breaker and limiter state stays isolated per endpoint. Keep that
endpoint-local shield single-attempt (breaker, limiter, timeout); put retry or hedging in the outer
shield so every additional send goes through safe replay and routing.

Handler options are setup objects. `ShieldDelegatingHandler` snapshots their scalar values,
delegates, routing values, and endpoint list when the handler pipeline is created; the direct
`AddShield(shield, options)` overload snapshots at registration. Mutating those source objects later
does not reconfigure existing handlers. Use a configuration-backed standard registration when
runtime changes are required; each valid reload publishes a fresh complete pipeline snapshot.

## Behaviour notes

- **Superseded responses are pipeline-owned.** Retry and hedging dispose failed or losing responses,
  including a loser that completes after the winner; the handler retains an idempotent safety net
  for custom strategies. A custom `OnRetry` disposal hook is unnecessary. `OnRetry` observes the
  live response, disposal completes before the next attempt starts, and the selected response
  remains caller-owned.
- **Redirects remain transport-owned.** Each Kevlar attempt begins with the original absolute URI (or its routed authority). Normal `HttpClientHandler` redirect policy runs inside that attempt.
- **Named-client state survives handler rotation.** Service-provider `AddShield` registrations and all standard registrations build or resolve one pipeline for that named client registration. Fixed-shield overloads already share their shield instance; request-selector overloads intentionally select a shield per request. Service-provider factories run once against the application provider, and circuit breakers, concurrency limiters, and endpoint caches are not multiplied when `HttpClientFactory` rotates handlers.
- **Configuration-backed state is replaced, not mutated.** Only a valid configuration reload publishes a fresh complete pipeline. Handler rotation reuses the current snapshot, and requests already executing retain the snapshot they captured at send start.
- **Standard hedging state is authority-local.** `AddStandardHedgeShield` creates one breaker and, when configured, one limiter per request authority or configured endpoint authority and preserves those instances across handler rotation until configuration reload replaces the pipeline.
- **Per-handler state remains explicit.** When fresh state for every handler lifetime is intentional, register `ShieldDelegatingHandler` directly with `AddHttpMessageHandler` and construct the shield inside that low-level handler factory.
- **Compose with other handlers normally.** The Kevlar handler is a regular `DelegatingHandler`; ordering relative to your own handlers follows the usual `AddHttpMessageHandler` rules.

:::tip Handling clause already done
`WhenTransient()` is a normal [handling clause](handling-failures.md) — everything you chain after it (retry, breaker, fallback) reacts to that transient-fault definition. Add your own `Or…`/`OrResult…` calls to extend it. The builder it returns is immutable, so one stored `WhenTransient()` can be branched into several pipelines without the branches leaking terms into each other.
:::

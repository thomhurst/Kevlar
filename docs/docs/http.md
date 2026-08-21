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
services.AddHttpClient("api")
    .AddStandardShield();
```

`AddStandardShield` wires up the pipeline you'd have built anyway (outermost first):

1. **30s total timeout** around everything
2. **3 jittered retries** (exponential from 250ms, capped 30s) — honouring `Retry-After` headers and disposing superseded responses
3. **Circuit breaker** — sampling mode: opens at a 50% failure ratio over a 30s window (minimum 10 calls), breaks for 15s
4. **10s attempt timeout** per individual try

## Bring your own pipeline

```csharp
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
            MaximumBufferSize = 1024 * 1024,
        });
```

You can also grab that exact shield directly with `HttpShield.Standard()`.

### `HttpShield.WhenTransient()`

Starts a typed `Shield<HttpResponseMessage>` builder with the standard transient-fault handling clause:

- `HttpRequestException`
- attempt timeouts (`TimeoutExceededException`)
- HTTP 500–599 responses (numeric status codes outside that range are not treated as 5xx)
- HTTP 408 (Request Timeout)
- HTTP 429 (Too Many Requests)

(The status-code test on its own is available as `HttpShield.IsTransient(response)`.)

### `HttpShield.RetryAfter`

A `DelayGenerator` for retry options: when the failed response carries a `Retry-After` header (delta or date form), the retry waits what the server asked for. The server's suggestion is used only when it's *longer* than the computed backoff; no header → normal backoff applies.

### Registering a shield built elsewhere

```csharp
services.AddHttpClient("api")
    .AddShield(sp => sp.GetRequiredService<IKevlarRegistry>()
        .GetShield<HttpResponseMessage>("downstream"));
```

`AddShield` accepts a shield instance or an `IServiceProvider` factory.

## Safe request replay

The first no-routing attempt sends the caller's original request directly. Additional attempts use
clones that preserve method, URI, HTTP version and version policy, request headers, request options,
and content headers. The handler owns every clone and every nonselected response; the caller owns
the original request and the returned response.

Content replay is explicit:

- `NoBuffer` (default) does no up-front body work. A request with content may be sent once; another
  attempt throws `HttpRequestReplayException` before reaching the transport.
- `Buffer` serializes content once before sending, bounded by `MaximumBufferSize`, then gives each
  attempt its own `ByteArrayContent`. Oversize or partial serialization fails before attempt 1.
- `RequestFactory` creates a complete fresh request per attempt. Use it for one-shot streams,
  generated bodies, signatures, or other request state that cannot be cloned. Factory requests are
  disposed by the handler.

GET, HEAD, OPTIONS, TRACE, PUT, and DELETE can replay automatically. POST, PATCH, and custom methods
require `AllowUnsafeMethodReplay = true` or a `RequestFactory`; only opt in when the operation is
actually idempotent. Timeouts and caller cancellation flow to every attempt and request factory.

## Endpoint-aware hedging

Route attempt 1, attempt 2, and so on across alternate authorities while preserving the original
path and query:

```csharp
var routing = new HttpEndpointRoutingOptions
{
    SelectionMode = HttpEndpointSelectionMode.Ordered,
    ShieldFactory = endpoint => HttpShield.WhenTransient()
        .CircuitBreaker(5, TimeSpan.FromSeconds(30)),
};
routing.Endpoints.Add(new HttpEndpoint(new Uri("https://api-a.example")));
routing.Endpoints.Add(new HttpEndpoint(new Uri("https://api-b.example")));

services.AddHttpClient("routed")
    .AddShield(
        HttpShield.WhenTransient().Hedge(2, TimeSpan.Zero),
        new ShieldHttpHandlerOptions { Routing = routing });
```

`Ordered` is deterministic configuration order. `Weighted` creates a deterministic weighted
permutation from `Seed`; a request visits every configured endpoint before cycling. `ShieldFactory`
is cached by authority, so circuit-breaker and limiter state stays isolated per endpoint. Keep that
endpoint-local shield single-attempt (breaker, limiter, timeout); put retry or hedging in the outer
shield so every additional send goes through safe replay and routing.

## Behaviour notes

- **Superseded responses are handler-owned.** The handler disposes failed retry responses and losing hedge responses, including a loser that completes after the winner. Do not add an `OnRetry` response-disposal hook when using `ShieldDelegatingHandler`. The selected response remains caller-owned.
- **Redirects remain transport-owned.** Each Kevlar attempt begins with the original absolute URI (or its routed authority). Normal `HttpClientHandler` redirect policy runs inside that attempt.
- **State sharing applies per registration.** `AddStandardShield` and `AddShield(shield)` build/capture one shield for that named client — every request through `"api"` shares the same circuit breaker, which is what makes the breaker meaningful. The factory overload runs once when `HttpClientFactory` creates a handler pipeline, receives that pipeline's service provider, and runs again only when the handler lifetime expires; return a shared instance, e.g. from the registry, to keep one circuit across lifetimes.
- **Compose with other handlers normally.** The Kevlar handler is a regular `DelegatingHandler`; ordering relative to your own handlers follows the usual `AddHttpMessageHandler` rules.

:::tip Handling clause already done
`WhenTransient()` is a normal [handling clause](handling-failures.md) — everything you chain after it (retry, breaker, fallback) reacts to that transient-fault definition. Add your own `WhenResult` calls to extend it.
:::

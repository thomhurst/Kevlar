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
    .AddShield(HttpShield.WhenTransient()
        .Retry(o =>
        {
            o.MaxRetries = 4;
            o.DelayGenerator = HttpShield.RetryAfter;   // server-driven delays
        })
        .CircuitBreaker(o => o.FailureRatio = 0.5));
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

## Behaviour notes

- **Superseded responses are disposed by the retry hook.** `Standard` disposes each replaced `HttpResponseMessage` in `OnRetry` so connections/buffers aren't leaked. The successful response, or final transient response after retries are exhausted, remains undisposed and belongs to the caller. If you build your own retry over responses, copy that trick: `o.OnRetry = static e => e.Outcome.Result?.Dispose();` (typed retry events carry the response as `Outcome.Result`)
- **Requests are resent, not cloned.** Retried and hedged attempts reuse the same `HttpRequestMessage`, preserving its method, URI, headers, options, and buffered content. Requests without content and rewindable content (`StringContent`, `ByteArrayContent`) are supported. A one-shot `StreamContent` is not rewound; a retry can fail while serializing it again, so buffer content yourself when retries are possible.
- **State sharing applies per registration.** `AddStandardShield` and `AddShield(shield)` build/capture one shield for that named client — every request through `"api"` shares the same circuit breaker, which is what makes the breaker meaningful. The factory overload runs once when `HttpClientFactory` creates a handler pipeline, receives that pipeline's service provider, and runs again only when the handler lifetime expires; return a shared instance, e.g. from the registry, to keep one circuit across lifetimes.
- **Compose with other handlers normally.** The Kevlar handler is a regular `DelegatingHandler`; ordering relative to your own handlers follows the usual `AddHttpMessageHandler` rules.

:::tip Handling clause already done
`WhenTransient()` is a normal [handling clause](handling-failures.md) — everything you chain after it (retry, breaker, fallback) reacts to that transient-fault definition. Add your own `WhenResult` calls to extend it.
:::

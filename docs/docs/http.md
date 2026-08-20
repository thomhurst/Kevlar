---
sidebar_position: 8
---

# HTTP Integration

The `Kevlar.Extensions.Http` package plugs policies into `HttpClientFactory` as a `DelegatingHandler`, with transient-fault handling and `Retry-After` support built in.

```bash
dotnet add package Kevlar.Extensions.Http
```

## The one-liner

```csharp
services.AddHttpClient("api")
    .AddStandardKevlar();
```

`AddStandardKevlar` wires up the pipeline you'd have built anyway (outermost first):

1. **30s total timeout** around everything
2. **3 jittered retries** (exponential from 250ms, capped 30s) — honouring `Retry-After` headers and disposing superseded responses
3. **Circuit breaker** — sampling mode: opens at a 50% failure ratio over a 30s window (minimum 10 calls), breaks for 15s
4. **10s attempt timeout** per individual try

## Bring your own pipeline

```csharp
services.AddHttpClient("api")
    .AddKevlar(HttpKevlar.HandleTransient()
        .Retry(o =>
        {
            o.MaxRetries = 4;
            o.DelayGenerator = HttpKevlar.RetryAfter;   // server-driven delays
        })
        .CircuitBreaker(o => o.FailureRatio = 0.5));
```

You can also grab that exact policy directly with `HttpKevlar.StandardPolicy()`.

### `HttpKevlar.HandleTransient()`

Starts a typed `Policy<HttpResponseMessage>` builder with the standard transient-fault handling clause:

- `HttpRequestException`
- attempt timeouts (`TimeoutExceededException`)
- HTTP 5xx responses
- HTTP 408 (Request Timeout)
- HTTP 429 (Too Many Requests)

(The status-code test on its own is available as `HttpKevlar.IsTransient(response)`.)

### `HttpKevlar.RetryAfter`

A `DelayGenerator` for retry options: when the failed response carries a `Retry-After` header (delta or date form), the retry waits what the server asked for. The server's suggestion is used only when it's *longer* than the computed backoff; no header → normal backoff applies.

### Registering a policy built elsewhere

```csharp
services.AddHttpClient("api")
    .AddKevlar(sp => sp.GetRequiredService<IKevlarRegistry>()
        .GetPolicy<HttpResponseMessage>("downstream"));
```

`AddKevlar` accepts a policy instance or an `IServiceProvider` factory.

## Behaviour notes

- **Superseded responses are disposed by the retry hook.** `StandardPolicy` disposes each replaced `HttpResponseMessage` in `OnRetry` so connections/buffers aren't leaked — only the final response reaches your code. If you build your own retry over responses, copy that trick: `o.OnRetry = static e => (e.Result as HttpResponseMessage)?.Dispose();`
- **Requests are resent, not cloned.** Retried and hedged attempts resend the same `HttpRequestMessage`. Safe for requests without content and for rewindable content (`StringContent`, `ByteArrayContent`); streamed one-shot content can't be resent.
- **State sharing applies per registration.** `AddStandardKevlar` and `AddKevlar(policy)` build/capture one policy for that named client — every request through `"api"` shares the same circuit breaker, which is what makes the breaker meaningful. (The factory overload runs per handler creation; return a shared instance, e.g. from the registry, to keep one circuit.)
- **Compose with other handlers normally.** The Kevlar handler is a regular `DelegatingHandler`; ordering relative to your own handlers follows the usual `AddHttpMessageHandler` rules.

:::tip Handling clause already done
`HandleTransient()` is a normal [handling clause](handling-failures.md) — everything you chain after it (retry, breaker, fallback) reacts to that transient-fault definition. Add your own `HandleResult` calls to extend it.
:::

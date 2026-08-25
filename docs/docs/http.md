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

The standard registration disables `HttpClient.Timeout`; its attempt and total timeout strategies
own timeout behavior inside the retry boundary. Configure `AttemptTimeout` and `TotalTimeout`
instead. If you configure `HttpClient.Timeout` on the same builder, call `AddStandardShield` after
that configuration.

Customize those stages without rebuilding the pipeline:

```csharp
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
        options.Handler.MaximumBufferSize = 256 * 1024;
    });
```

`StandardHttpShieldOptions` exposes the total timeout, typed retry and circuit-breaker options,
optional concurrency limiter, attempt timeout, and handler replay/routing options. Invalid strategy
values fail while the registration is built; handler replay/routing values fail when
`HttpClientFactory` builds its handler pipeline, before a request is sent.

The breaker is a `CircuitBreakerOptions<HttpResponseMessage>`, so `HandlesResult` can replace the
standard transient-result clause for that stage without changing retry handling.

For dependency-aware setup, use the service-provider overload:

```csharp
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

The one-argument callback runs during registration and its shield is shared across handler
rotations, matching parameterless `AddStandardShield()`. The service-provider callback runs once
per `HttpClientFactory` handler lifetime and creates fresh strategy state for that lifetime.

## Configuration and reload

Pass an `IConfiguration` section to bind the standard pipeline and reload it when the section's
change token fires:

```csharp
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Http:Api:TotalTimeout"] = "00:00:20",
        ["Http:Api:Retry:MaxRetries"] = "2",
        ["Http:Api:Retry:Backoff"] = "Exponential",
        ["Http:Api:Retry:BaseDelay"] = "00:00:00.100",
        ["Http:Api:Retry:Jitter"] = "Equal",
        ["Http:Api:AttemptTimeout"] = "00:00:05",
        ["Http:Api:Handler:MaximumBufferSize"] = "262144",
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
`BackoffMaxDelay`, and `MaxDelay`. Circuit
breaker, concurrency-limit, handler, routing, and endpoint keys match their public option-property
names. Endpoint entries accept either a URI scalar or `Uri` plus optional `Weight` children.

Configuration is applied first. The service-provider callback overload runs afterward, so DI values
and delegates can deliberately override bound values:

```csharp
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
            logger.LogWarning("Retry {RetryNumber}", retry.RetryNumber);
    });
```

Each request captures one immutable shield-and-handler snapshot. A valid reload builds the whole
replacement before publishing it atomically; in-flight requests finish on their original snapshot.
Invalid binding or validation keeps the last valid snapshot and calls `onReloadFailure` with the
full configuration path. A successful reload starts fresh breaker, limiter, and endpoint-local
state. `HttpClientFactory` handler rotation also creates fresh state and reruns the
service-provider callback; disposed handlers unsubscribe from configuration changes.

Hedging uses the same reload contract. Its scalar keys match `StandardHedgeShieldOptions`, and
`Endpoints` is required:

```csharp
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["MaxAttempts"] = "2",
        ["HedgeDelay"] = "00:00:00.500",
        ["SelectionMode"] = "Weighted",
        ["Endpoints:0:Uri"] = "https://api-a.example",
        ["Endpoints:0:Weight"] = "3",
        ["Endpoints:1:Uri"] = "https://api-b.example",
    })
    .Build();

services.AddHttpClient("routed")
    .AddStandardHedgeShield(configuration);
```

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

A `DelayGenerator` for retry options: when the failed response carries a `Retry-After` header (delta or date form), the retry waits what the server asked for. The server's suggestion is used only when it's *longer* than the computed backoff; no header → normal backoff applies.

The standard shield caps every retry delay at 10 seconds, so one excessive server suggestion cannot
impose an unbounded wait. Custom shields can cap server-suggested delays directly:

```csharp
var shield = HttpShield.WhenTransient()
    .Retry(options =>
    {
        options.DelayGenerator = HttpShield.RetryAfter(TimeSpan.FromSeconds(5));
    });
```

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

Replay behavior depends on the request:

- `NoBuffer` (default) reuses inherently re-readable content such as `ByteArrayContent`,
  `StringContent`, and `FormUrlEncodedContent`. Positive-length content already loaded into its HTTP
  buffer is also reusable. A fresh `JsonContent` or one-shot content such as `StreamContent` is sent
  once; call `LoadIntoBufferAsync()` first, select `Buffer`, or provide a `RequestFactory` to replay it.
- `Buffer` serializes content once before sending, bounded by `MaximumBufferSize`, then gives each
  attempt its own `ByteArrayContent`. Oversize or partial serialization fails before attempt 1.
- `RequestFactory` creates a complete fresh request per attempt. Use it for one-shot streams,
  generated bodies, signatures, or other request state that cannot be cloned. Factory requests are
  disposed by the handler.

GET, HEAD, OPTIONS, TRACE, PUT, and DELETE can replay automatically. POST, PATCH, and custom methods
require `AllowUnsafeMethodReplay = true` or a `RequestFactory`; only opt in when the operation is
actually idempotent. If method or content cannot be replayed safely, retry and hedging remain
single-attempt: the original response is returned or the original exception is rethrown without a
retry delay or callback. Other stages, including timeout, circuit breaker, and concurrency limiting,
still observe that attempt. `HttpRequestReplayException` is reserved for configuration failures such
as a null factory result or content exceeding the requested buffer limit. Timeouts and caller
cancellation flow to every attempt and request factory.

## Endpoint-aware hedging

Route attempt 1, attempt 2, and so on across alternate authorities while preserving the original
path and query:

```csharp
services.AddHttpClient("routed")
    .AddStandardHedgeShield(options =>
    {
        options.Endpoints.Add(new HttpEndpoint(new Uri("https://api-a.example"), weight: 3));
        options.Endpoints.Add(new HttpEndpoint(new Uri("https://api-b.example"), weight: 1));
        options.SelectionMode = HttpEndpointSelectionMode.Weighted;
        options.MaxAttempts = 2;
        options.HedgeDelay = TimeSpan.FromMilliseconds(500);
        options.HedgeDelayGenerator = hedge => hedge.Elapsed < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.Zero;
    });
```

`AddStandardHedgeShield` installs a 30s total timeout and up to two hedged attempts. Each endpoint
gets its own 10-concurrent/zero-queue limiter, 50%-over-30s circuit breaker (minimum 10 attempts,
15s break), and 10s attempt timeout. Configure those defaults through `TotalTimeout`, `MaxAttempts`,
`HedgeDelay`, `HedgeDelayGenerator`, `HedgeDelayGeneratorAsync`, `MaxConcurrency`, `QueueLimit`,
`FailureRatio` or `ConsecutiveFailures`,
`MinimumThroughput`, `SamplingWindow`, `BreakDuration`, and `AttemptTimeout`.

The registration also exposes `ContentReplayPolicy`, `MaximumBufferSize`,
`AllowUnsafeMethodReplay`, and `RequestFactory`. POST, PATCH, and custom methods still require the
same explicit idempotency opt-in described above; registering the standard hedging pipeline does not
make an unsafe operation safe to repeat.

For a fully custom endpoint-aware pipeline, compose the outer and endpoint shields directly:

```csharp
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
        HttpShield.WhenTransient().Hedge(2, delay: TimeSpan.Zero),
        new ShieldHttpHandlerOptions { Routing = routing });
```

`Ordered` is deterministic configuration order. `Weighted` creates a deterministic weighted
permutation from `Seed`; a request visits every configured endpoint before cycling. `ShieldFactory`
is cached by authority, so circuit-breaker and limiter state stays isolated per endpoint. Keep that
endpoint-local shield single-attempt (breaker, limiter, timeout); put retry or hedging in the outer
shield so every additional send goes through safe replay and routing.

## Behaviour notes

- **Superseded responses are handler-owned.** The handler disposes failed retry responses and losing hedge responses, including a loser that completes after the winner. A custom `OnRetry` response-disposal hook is unnecessary with `ShieldDelegatingHandler`; the hook that `HttpShield.Standard()` installs stays safe because `HttpResponseMessage.Dispose` is idempotent. The selected response remains caller-owned.
- **Redirects remain transport-owned.** Each Kevlar attempt begins with the original absolute URI (or its routed authority). Normal `HttpClientHandler` redirect policy runs inside that attempt.
- **State sharing depends on registration form.** Parameterless `AddStandardShield()`, its one-argument options callback, and `AddShield(shield)` build/capture one shield for that named client, so state survives handler rotation. Service-provider callbacks run once per `HttpClientFactory` handler lifetime and create fresh state unless they resolve and return shared state from DI.
- **Configuration-backed state is replaced, not mutated.** Reload and handler rotation publish fresh complete pipelines. Requests already executing retain the snapshot they captured at send start.
- **Standard hedging state is endpoint-local.** `AddStandardHedgeShield` creates one limiter and breaker per authority in each `HttpClientFactory` handler pipeline and reuses them across requests for that handler's lifetime.
- **Compose with other handlers normally.** The Kevlar handler is a regular `DelegatingHandler`; ordering relative to your own handlers follows the usual `AddHttpMessageHandler` rules.

:::tip Handling clause already done
`WhenTransient()` is a normal [handling clause](handling-failures.md) — everything you chain after it (retry, breaker, fallback) reacts to that transient-fault definition. Add your own `Or…`/`OrResult…` calls to extend it. The builder it returns is immutable, so one stored `WhenTransient()` can be branched into several pipelines without the branches leaking terms into each other.
:::

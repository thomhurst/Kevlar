---
sidebar_position: 12
---

# For Library Authors

You ship a library that talks to something flaky — an HTTP API, a database, a queue — and you want *your users* to decide how resilient those calls are. The integration surface is one parameter:

<!-- doc-test-ignore: Library type depends on the author's Report model and FetchReportAsync transport. -->
```csharp
public sealed class ReportsClient
{
    private readonly HttpClient _http;
    private readonly Shield _shield;

    public ReportsClient(HttpClient http, Shield? shield = null)
    {
        _http = http;
        _shield = shield ?? Shield.Empty;   // pass-through when the user doesn't care
    }

    public ValueTask<Report> GetReportAsync(string id, CancellationToken ct = default) =>
        _shield.ExecuteAsync((_http, id),
            static (s, token) => FetchReportAsync(s._http, s.id, token), ct);
}
```

Your users compose whatever pipeline they want and hand it over:

```csharp
var client = new ReportsClient(http,
    Shield
        .Timeout(TimeSpan.FromSeconds(30))
        .Retry(3)
        .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30)));
```

`Shield.Empty` executes the delegate directly with no strategies in between, so the null-coalescing default costs nothing and needs no branching at call sites.

## Why not an interface?

The instinct to expose `IShield` comes from the "many implementations behind one contract" world. A shield doesn't have that shape: **users vary the pipeline's *contents*, not its *type***. Every mix of retry, breaker, timeout and hedging is the same concrete `Shield` — the polymorphism lives in the strategy chain inside it, not at the parameter.

This is the same conclusion Polly v8 reached: v7's `IAsyncPolicy` interfaces were dropped in favour of the concrete `ResiliencePipeline`, because no meaningful second implementation ever existed and the interface only bought virtual dispatch on the hot path.

For the same reason there is no `Kevlar.Abstractions` package. Kevlar keeps its dependency graph small, and `Shield` is already the abstraction consumed by library code.

And for testing, you don't need a mock: `Shield.Empty` is the no-op, and fault injection works better through a real shield with a [custom strategy](custom-strategies.md) — it exercises the actual engine. See [Testing Your Shields](testing.md).

If your library ships a reactive custom strategy, accept its active handling through the `Use` factory rather than baking exception rules into the package:

<!-- doc-test-ignore: LibraryRetryStrategy is supplied by the library author. -->
```csharp
var shield = Shield.When<HttpRequestException>()
    .Use(clause => new LibraryRetryStrategy(clause));
```

Store the supplied `HandlingClause`, consult it for each `Outcome<T>`, and override `Strategy.Handling` so chain validation and `Kevlar.Testing` can inspect the declaration. Proactive custom strategies can keep using `Use(Strategy)`.

## Adapter exception proxies

An adapter may need a private exception wrapper to retain transport bookkeeping while a strategy
chooses an attempt. Derive that wrapper from `KevlarProxyException` so handling clauses and public
`Outcome<T>` APIs see the original failure without using `Exception.Data`:

<!-- doc-test-declaration -->
```csharp
public sealed class TransportAttemptException(Exception originalException)
    : KevlarProxyException(originalException);
```

Kevlar keeps the wrapper inside pipeline execution, but `Outcome<T>.Exception`,
`GetResultOrRethrow()`, and reactive predicates expose `OriginalException`.

## Result-aware parameters

If callers should be able to react to *result values* — retry on `null`, hedge on an error status — accept a `Shield<T>` instead:

<!-- doc-test-ignore: Constructor fragment intended to appear inside the library's ProfileClient type. -->
```csharp
public ProfileClient(Shield<Profile?>? shield = null)
    => _shield = shield ?? Shield<Profile?>.Empty;
```

```csharp
var client = new ProfileClient(
    Shield.For<Profile?>().WhenResultIsNull().Or<HttpRequestException>().Retry(3));
```

## Opinionated defaults

`Shield.Empty` is the right default when resilience is genuinely optional. If your library *should* retry out of the box, default to a real shield instead — and think about where its state lives:

<!-- doc-test-ignore: Constructor fragment intended to appear inside the library's ReportsClient type. -->
```csharp
public ReportsClient(HttpClient http, Shield? shield = null)
    => _shield = shield ?? Shield.Retry(3).Timeout(TimeSpan.FromSeconds(10));
```

Built per instance like this, each client gets fresh strategy state. Hoist the default into a `static readonly` field and every client that falls back to it shares one instance — which matters the moment the default includes a circuit breaker or rate limit, because [state lives with the shield instance](composition.md#the-state-sharing-rule). Per-instance breakers guard each client separately; a shared one trips for all of them together. Pick deliberately and say which in your docs.

## Enforcing your own invariants

Accepting a user shield doesn't mean giving up your own protections. `Wrap` composes their pipeline around yours:

```csharp
// The user's strategies run outermost; your attempt timeout always applies inside them.
var effective = (shield ?? Shield.Empty)
    .Wrap(Shield.Timeout(TimeSpan.FromSeconds(5)));
```

Each attempt the user's retries make still runs through your inner timeout — they shape the outer behaviour, you keep the last line of defence.

## Dependency injection

Nothing extra is needed on your side: a `Shield` constructor parameter resolves like any other dependency. Users on `Microsoft.Extensions.DependencyInjection` can register named shields and bind them from configuration via [`Kevlar.Extensions.DependencyInjection`](dependency-injection.md), then pass them to your library as [keyed services](dependency-injection.md#consuming-as-a-keyed-service).

## Assembly identity and strong naming

Kevlar assemblies are intentionally not strong-named. The core package depends on Reservoir, which
is not strong-named, and .NET Framework does not allow a strong-named assembly to reference an
unsigned dependency. Consequently, a strong-named .NET Framework library cannot reference Kevlar.
Applications and unsigned libraries on .NET Framework, plus all consumers on modern .NET, are not
affected.

Strong names establish assembly identity, not publisher trust or code security. Adding one would
change Kevlar's assembly identity and therefore requires a major release. Assembly versions are
pinned to the package major (`1.0.0.0` throughout the 1.x line), so minor and patch releases do not
create binding-redirect churn if the signing decision changes in a future major release. See
[Microsoft's strong-naming guidance](https://learn.microsoft.com/dotnet/standard/library-guidance/strong-naming).

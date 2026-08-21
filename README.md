# Kevlar

**Fast, allocation-conscious resilience for .NET.** Retries, circuit breakers, timeouts, rate limiting, concurrency limiting, hedging and fallbacks — composed through one fluent Shield API.

```csharp
using Kevlar;

var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))                    // total budget for the whole operation
    .Retry(3)                                             // exponential backoff + jitter, out of the box
    .CircuitBreaker(5, breakDuration: TimeSpan.FromSeconds(30));

var user = await shield.ExecuteAsync(ct => LoadUserAsync(id, ct), cancellationToken);
```

Build a shield once, reuse it everywhere. Shields are **immutable and thread-safe**, and ordinary `Task`-returning methods flow straight in — no `ValueTask` wrapping.

And when the scenario stops being simple, the API doesn't change shape — it deepens. Typed handling clauses, options overloads and delegate hooks carry the same fluent chain all the way up:

```csharp
var monitor = new CircuitBreakerMonitor();

var search = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .OrResult(r => (int)r.StatusCode is >= 500 or 429)               // results are failures too
    .Fallback((outcome, ct) => cache.GetCachedResultsAsync(ct))      // last resort — sees exactly what failed
    .Retry(o =>
    {
        o.MaxRetries = 4;
        o.Backoff = Backoff.Exponential(TimeSpan.FromMilliseconds(200), maxDelay: TimeSpan.FromSeconds(5));
        o.DelayGenerator = e => e.Outcome.Result?.Headers.RetryAfter?.Delta;   // server knows best…
        o.MaxDelay = TimeSpan.FromSeconds(10);                                 // …within reason
        o.OnRetry = e => logger.LogWarning("search retry {Attempt} in {Delay}", e.Attempt, e.Delay);
    })
    .CircuitBreaker(o =>
    {
        o.FailureRatio = 0.5;                            // open at ≥50% failures…
        o.MinimumThroughput = 20;                        // …across ≥20 calls…
        o.SamplingWindow = TimeSpan.FromSeconds(30);     // …in a rolling 30s window
        o.Monitor = monitor;                             // ops handle: monitor.Isolate() / monitor.Reset()
    })
    .Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(150))    // race a second attempt on slow p99s
    .Timeout(TimeSpan.FromSeconds(2))                                // per-attempt budget
    .WithName("search");                                             // tags metrics and ToString()
```

One clause up top decides what "failure" means for every strategy below it — exceptions and result values alike. The result is still just a `Shield<HttpResponseMessage>`: immutable, reusable, and it prints its own pipeline when you log it.

## Why Kevlar?

- **Intuitive first.** `Shield.When<TimeoutException>().Retry(3)` reads like what it does. No context pooling ceremony, no predicate-builder classes, no options objects for the simple cases — and full options objects when you want them.
- **Fast.** Outcomes flow between pipeline layers as structs instead of thrown exceptions; contexts are pooled internally; state-passing overloads eliminate closures; `ValueTask` end to end.
- **Production defaults.** `Shield.Retry(3)` gives you exponential backoff *with jitter* capped at 30s — the thing you'd have configured anyway.
- **Hard to hold wrong.** Impossible chain orders throw at build time with the fix in the message, and the `Kevlar.Analyzers` package flags delegates that ignore their `CancellationToken` at compile time.
- **Observable out of the box.** `shield.ToString()` prints the whole pipeline; every shield publishes metrics through a built-in `Meter` — no telemetry package, no setup.
- **Composable.** Shields merge with `Wrap` and `Compose`, chain fluently, and stateful strategies (breakers, limiters) intentionally share their state wherever the same shield instance is reused.
- **Broad reach.** `netstandard2.0` (covers .NET Framework 4.6.2+) and `net10.0` targets.

## Packages

| Package | Purpose |
|---|---|
| `Kevlar` | The core: all strategies |
| `Kevlar.Extensions.DependencyInjection` | Named shields, config-bound shields + `IKevlarRegistry` for Microsoft DI |
| `Kevlar.Extensions.Http` | `HttpClientFactory` integration, transient-fault handling, `Retry-After` support |
| `Kevlar.Analyzers` | Roslyn analyzers that catch resilience mistakes at compile time |

## The five-minute tour

### Handling clauses

Tell reactive strategies (retry, circuit breaker, hedging, fallback) what counts as a failure — `When` starts a clause, `Or` extends it:

```csharp
// Exceptions
var shield = Shield
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .OrWhen(ex => ex is IOException { Message: var m } && m.Contains("pipe"))
    .Retry(5);

// Results too — lift into a typed shield with For<T>
var http = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode >= 500)
    .Retry(3);

// The most common result check has a shorthand:
Shield.For<User?>().WhenDefault().Retry(2);   // retry null results
```

With no handling clause, the default is: **any exception except `OperationCanceledException`**. A clause applies to the strategy it creates *and* to every reactive strategy chained after it — through `For<T>()`, `Wrap` and `Compose` too — until you write a new clause.

### Retry

```csharp
Shield.Retry(3);                                          // exponential + jitter (250ms base, 30s cap)
Shield.Retry(3, Backoff.Constant(TimeSpan.FromSeconds(1)));
Shield.Retry(3, Backoff.Linear(TimeSpan.FromMilliseconds(500)));
Shield.RetryForever(Backoff.Exponential(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromMinutes(1)));

Shield.Retry(o =>
{
    o.MaxRetries = 5;
    o.Backoff = Backoff.Custom(attempt => TimeSpan.FromMilliseconds(100 * attempt));
    o.MaxDelay = TimeSpan.FromSeconds(10);   // absolute cap — even over DelayGenerator output
    o.OnRetry = e => logger.LogWarning(e.Exception, "Retry {Attempt} after {Delay}", e.Attempt, e.Delay);
    o.DelayGenerator = e => /* return a TimeSpan to override the computed delay, or null */ null;
});
```

On a typed `Shield<T>`, retry events are typed too: `e.Outcome` is an `Outcome<T>` — no boxed `object` results, no casting.

### Circuit breaker

```csharp
// Simple: open after N consecutive failures
Shield.CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));

// Sampling: open when ≥50% of calls fail within a rolling window
var monitor = new CircuitBreakerMonitor();
Shield.CircuitBreaker(o =>
{
    o.FailureRatio = 0.5;
    o.MinimumThroughput = 20;
    o.SamplingWindow = TimeSpan.FromSeconds(30);
    o.BreakDuration = TimeSpan.FromSeconds(15);
    o.Monitor = monitor;                                  // observe + manual control
    o.OnStateChanged = c => logger.LogWarning("Circuit {From} -> {To}", c.From, c.To);
});

_ = monitor.State;  // Closed / Open / HalfOpen / Isolated
monitor.Isolate();  // force open (maintenance switch)
monitor.Reset();    // close and clear metrics
```

Open circuits reject with `CircuitOpenException` (carrying `RetryAfter`). After the break duration, one probe execution decides whether to close or re-open.

### Timeout

```csharp
Shield.Timeout(TimeSpan.FromSeconds(10));
```

Cooperative: the delegate receives a cancellation token that fires on timeout — always use the token you're handed (the `Kevlar.Analyzers` package warns when you don't). Exceeding the budget surfaces `TimeoutExceededException`, which retry clauses can handle.

### Rate limit & concurrency limit

```csharp
Shield.RateLimit(100, perWindow: TimeSpan.FromSeconds(1));   // token bucket, burst = 100
Shield.RateLimit(o => { o.Permits = 100; o.Window = TimeSpan.FromSeconds(1); o.QueueLimit = 20; });

Shield.ConcurrencyLimit(maxConcurrency: 10, maxQueue: 20);   // the classic bulkhead pattern
```

Rejections throw `RateLimitExceededException` (with a `RetryAfter` estimate) and `ConcurrencyLimitExceededException`. With `QueueLimit > 0`, rate-limited executions wait for their reserved permit instead of failing.

### Hedging

```csharp
// Fire a second attempt if the first hasn't answered within 100ms; fastest wins, losers are cancelled.
Shield.Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(100));
```

`Delay = TimeSpan.Zero` races all attempts at once; `Timeout.InfiniteTimeSpan` hedges only on failure. A handled failure always launches the next attempt immediately. Your delegate must be safe to invoke concurrently.

### Fallback

```csharp
var shield = Shield.For<Config>()
    .When<HttpRequestException>()
    .Fallback(Config.Default);

// Or compute it, with access to the typed failure:
var computed = Shield.For<Config>()
    .When<HttpRequestException>()
    .Fallback((outcome, ct) =>
    {
        logger.LogError(outcome.Exception, "Using cached config");
        return new ValueTask<Config>(cache.Get());
    });

// Void executions have their own fallback on the plain Shield:
Shield.When<MessagingException>()
    .Fallback((exception, ct) => deadLetter.PublishAsync(exception, ct));
```

Fallback belongs **before** the strategies it recovers from (first = outermost). Chain it after a retry with the same clause and Kevlar throws at build time — that order silently disables the retry, so it refuses to build it.

## Composition

**The first strategy in a chain is the outermost** — the same rule as ASP.NET middleware:

```csharp
Shield
    .Timeout(TimeSpan.FromSeconds(30))   // 1. total budget around everything below
    .Retry(3)                            // 2. retries happen inside that budget
    .CircuitBreaker(5, TimeSpan.FromSeconds(30))
    .Timeout(TimeSpan.FromSeconds(5));   // 4. each individual attempt gets 5s
```

Merge independently defined shields:

```csharp
var breaker  = Shield.CircuitBreaker(5, TimeSpan.FromSeconds(30));   // built once — holds the circuit state
var reads    = Shield.Retry(3).Wrap(breaker);
var writes   = Shield.Timeout(TimeSpan.FromSeconds(5)).Wrap(breaker);
// reads and writes share ONE circuit: failures through either trip both.

var combined = Shield.Compose(timeoutShield, retryShield, breakerShield);  // first = outermost
```

That's the state-sharing rule in one line: **strategy state lives with the shield instance that created it**. Reuse the instance to share a circuit or a rate limiter; build a new one for fresh state.

Every shield describes itself — log it at startup:

```csharp
logger.LogInformation("using {Shield}", shield);
// github: Timeout(30s) → Retry(3, exponential 250ms ×2 +jitter ≤30s) → CircuitBreaker(5 consecutive, break 30s)
```

## Executing

```csharp
await shield.ExecuteAsync(ct => FetchAsync(ct), cancellationToken);   // Task or ValueTask — both just work
await shield.ExecuteAsync(ct => SaveAsync(ct), cancellationToken);    // async void
shield.Execute(ct => ComputeSync(ct));                                // sync (same shield!)

// Zero-closure hot path: thread your state instead of capturing it
await shield.ExecuteAsync((client, id), static (s, ct) => s.client.GetUserAsync(s.id, ct), ct);

// No-throw execution: inspect the outcome instead
Outcome<User> outcome = await shield.ExecuteOutcomeAsync(ct => LoadAsync(ct));
if (!outcome.IsSuccess) logger.LogError(outcome.Exception, "gave up");
```

The same shield serves any result type, sync or async. (One exception: hedging is inherently concurrent and requires async execution.)

## Dependency injection

<!-- doc-test-tail-declaration: split-before=public sealed class -->
```csharp
services.AddShield("github", Shield.Timeout(TimeSpan.FromSeconds(10)).Retry(3));
services.AddShield<HttpResponseMessage>("downstream",
    sp => HttpShield.WhenTransient().Retry(3).WithName("downstream"));

// Or bind the whole shield from configuration — tunable without a redeploy:
services.AddShield("github", builder.Configuration.GetSection("Resilience:GitHub"));

// Consume via the registry…
var shield = registry.GetShield("github");                       // IKevlarRegistry
// …or as a keyed service
public sealed class GitHubClient([FromKeyedServices("github")] Shield shield)
{
    public Shield Resilience { get; } = shield;
}
```

## HTTP

```csharp
services.AddHttpClient("api")
    .AddStandardShield();     // 30s total timeout → 3 jittered retries (honouring Retry-After,
                              // disposing retried responses) → circuit breaker → 10s attempt timeout

// Or bring your own:
services.AddHttpClient("api")
    .AddShield(HttpShield.WhenTransient()        // HttpRequestException, attempt timeouts, 5xx, 408, 429
        .Retry(o => { o.MaxRetries = 4; o.DelayGenerator = HttpShield.RetryAfter; })
        .CircuitBreaker(o => o.FailureRatio = 0.5));
```

## Observability

On .NET 8+ every shield publishes metrics through a `Meter` named `"Kevlar"` with zero configuration — executions, retries, timeouts, hedges, fallbacks, rejections and circuit transitions, tagged with the shield's `WithName` name. Subscribe with `AddMeter(KevlarDiagnostics.MeterName)`.

And `dotnet add package Kevlar.Analyzers` adds compile-time checks — starting with KEV001: an execution delegate that ignores the `CancellationToken` it is handed (the most common way to defeat a timeout).

## Custom strategies

Everything in Kevlar is a `Strategy` — middleware over an `Outcome<T>` pipeline. Write your own:

<!-- doc-test-declaration: split-before=var shield -->
```csharp
public sealed class LoggingStrategy(ILogger logger) : Strategy
{
    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next, KevlarContext context)
    {
        var start = context.TimeProvider.GetTimestamp();
        var outcome = await next.InvokeAsync(context);
        logger.LogInformation("{Shield} took {Elapsed}", context.ShieldName,
            context.TimeProvider.GetElapsedTime(start));
        return outcome;
    }

    public override string Describe() => "Logging";
}

var shield = Shield.Use(new LoggingStrategy(logger)).Retry(3);
```

Strategies return failures as outcomes rather than throwing, so outer strategies can react to them. Invoke `next` zero times (short-circuit), once (decorate), or many times (retry/hedge).

## Testing your shields

Every delay, timeout and time window runs on a `TimeProvider`:

```csharp
var time = new FakeTimeProvider();   // Microsoft.Extensions.TimeProvider.Testing
var shield = Shield.Retry(3, Backoff.Constant(TimeSpan.FromSeconds(10))).WithTimeProvider(time);

var pending = shield.ExecuteAsync(ct => FlakyAsync(ct)).AsTask();
time.Advance(TimeSpan.FromSeconds(10));                  // no real waiting in tests
```

## Coming from Polly?

| Polly v8 | Kevlar |
|---|---|
| `new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { … }).Build()` | `Shield.Retry(3)` |
| `ShouldHandle = new PredicateBuilder().Handle<T>()` | `Shield.When<T>().…` (ambient for the whole chain) |
| `ResiliencePipeline` / `ResiliencePipeline<T>` | `Shield` / `Shield<T>` |
| `ResilienceContextPool.Shared.Get(...)` + `Return` | automatic — contexts are pooled internally |
| `BrokenCircuitException` | `CircuitOpenException` (with `RetryAfter`) |
| `TimeoutRejectedException` | `TimeoutExceededException` |
| `CircuitBreakerManualControl` + `StateProvider` | one `CircuitBreakerMonitor` |
| `AddConcurrencyLimiter(10, 20)` | `Shield.ConcurrencyLimit(10, maxQueue: 20)` |
| Delegates must return `ValueTask` | `Task`-returning methods flow straight in |
| Retry default: constant 2s, no jitter | exponential + jitter, 30s cap |
| First strategy added is outermost | same rule — pipelines translate 1:1 |

## Performance

Kevlar is benchmarked against Polly v8 across every strategy on every merge to `main` — happy paths, failure paths, and composed pipelines. The results are published automatically to the [Benchmarks page](https://thomhurst.github.io/Kevlar/docs/benchmarks) in the docs.

# Kevlar

**Fast, zero-dependency resilience for .NET.** Retries, circuit breakers, timeouts, rate limiting, bulkheads, hedging and fallbacks — composed through one fluent, allocation-conscious policy API.

```csharp
using Kevlar;

var policy = Policy
    .Timeout(TimeSpan.FromSeconds(30))                    // total budget for the whole operation
    .Retry(3)                                             // exponential backoff + jitter, out of the box
    .CircuitBreaker(5, breakDuration: TimeSpan.FromSeconds(30));

var user = await policy.ExecuteAsync(ct => LoadUserAsync(id, ct), cancellationToken);
```

Build a policy once, reuse it everywhere. Policies are **immutable and thread-safe**.

## Why Kevlar?

- **Intuitive first.** `Policy.Handle<TimeoutException>().Retry(3)` reads like what it does. No context pooling ceremony, no predicate-builder classes, no options objects for the simple cases — and full options objects when you want them.
- **Fast.** Outcomes flow between pipeline layers as structs instead of thrown exceptions; contexts are pooled internally; state-passing overloads eliminate closures; `ValueTask` end to end.
- **Production defaults.** `Policy.Retry(3)` gives you exponential backoff *with jitter* capped at 30s — the thing you'd have configured anyway.
- **Composable.** Policies merge with `Wrap` and `Compose`, chain fluently, and stateful strategies (breakers, limiters) intentionally share their state wherever the same policy instance is reused.
- **Broad reach.** `netstandard2.0` (covers .NET Framework 4.6.2+) and `net8.0` targets. The core has zero third-party dependencies.

## Packages

| Package | Purpose |
|---|---|
| `Kevlar` | The core: all strategies, zero dependencies |
| `Kevlar.Extensions.DependencyInjection` | Named policies + `IKevlarRegistry` for Microsoft DI |
| `Kevlar.Extensions.Http` | `HttpClientFactory` integration, transient-fault handling, `Retry-After` support |

## The five-minute tour

### Handling clauses

Tell reactive strategies (retry, circuit breaker, hedging, fallback) what counts as a failure:

```csharp
// Exceptions
var policy = Policy
    .Handle<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .OrWhen(ex => ex is IOException { Message: var m } && m.Contains("pipe"))
    .Retry(5);

// Results too — lift into a typed policy with For<T>
var http = Policy.For<HttpResponseMessage>()
    .Handle<HttpRequestException>()
    .HandleResult(r => (int)r.StatusCode >= 500)
    .Retry(3);
```

With no handling clause, the default is: **any exception except `OperationCanceledException`**. A clause applies to the strategy it creates *and* to every reactive strategy chained after it, until you write a new clause.

### Retry

```csharp
Policy.Retry(3);                                          // exponential + jitter (250ms base, 30s cap)
Policy.Retry(3, Backoff.Constant(TimeSpan.FromSeconds(1)));
Policy.Retry(3, Backoff.Linear(TimeSpan.FromMilliseconds(500)));
Policy.RetryForever(Backoff.Exponential(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromMinutes(1)));

Policy.Retry(o =>
{
    o.MaxRetries = 5;
    o.Backoff = Backoff.Custom(attempt => TimeSpan.FromMilliseconds(100 * attempt));
    o.MaxDelay = TimeSpan.FromSeconds(10);
    o.OnRetry = e => logger.LogWarning(e.Exception, "Retry {Attempt} after {Delay}", e.Attempt, e.Delay);
    o.DelayGenerator = e => /* return a TimeSpan to override the computed delay, or null */ null;
});
```

### Circuit breaker

```csharp
// Simple: open after N consecutive failures
Policy.CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));

// Sampling: open when ≥50% of calls fail within a rolling window
var monitor = new CircuitBreakerMonitor();
Policy.CircuitBreaker(o =>
{
    o.FailureRatio = 0.5;
    o.MinimumThroughput = 20;
    o.SamplingWindow = TimeSpan.FromSeconds(30);
    o.BreakDuration = TimeSpan.FromSeconds(15);
    o.Monitor = monitor;                                  // observe + manual control
    o.OnStateChanged = c => logger.LogWarning("Circuit {From} -> {To}", c.From, c.To);
});

monitor.State;      // Closed / Open / HalfOpen / Isolated
monitor.Isolate();  // force open (maintenance switch)
monitor.Reset();    // close and clear metrics
```

Open circuits reject with `CircuitOpenException` (carrying `RetryAfter`). After the break duration, one probe execution decides whether to close or re-open.

### Timeout

```csharp
Policy.Timeout(TimeSpan.FromSeconds(10));
```

Cooperative: the delegate receives a cancellation token that fires on timeout — always use the token you're handed. Exceeding the budget surfaces `TimeoutExceededException` (which retry clauses can handle: `Policy.Handle<TimeoutExceededException>().Retry(2).Timeout(...)`).

### Rate limit & bulkhead

```csharp
Policy.RateLimit(100, perWindow: TimeSpan.FromSeconds(1));   // token bucket, burst = 100
Policy.RateLimit(o => { o.Permits = 100; o.Window = TimeSpan.FromSeconds(1); o.QueueLimit = 20; });

Policy.Bulkhead(maxConcurrency: 10, maxQueue: 20);           // concurrency isolation
```

Rejections throw `RateLimitExceededException` (with a `RetryAfter` estimate) and `BulkheadRejectedException`. With `QueueLimit > 0`, rate-limited executions wait for their reserved permit instead of failing.

### Hedging

```csharp
// Fire a second attempt if the first hasn't answered within 100ms; fastest wins, losers are cancelled.
Policy.Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(100));
```

`Delay = TimeSpan.Zero` races all attempts at once; `Timeout.InfiniteTimeSpan` hedges only on failure. A handled failure always launches the next attempt immediately. Your delegate must be safe to invoke concurrently.

### Fallback

```csharp
var policy = Policy.For<Config>()
    .Handle<HttpRequestException>()
    .Fallback(Config.Default);

// Or compute it, with access to the failure:
.Fallback((outcome, ct) =>
{
    logger.LogError(outcome.Exception, "Using cached config");
    return new ValueTask<Config>(cache.Get());
});
```

## Composition

**The first strategy in a chain is the outermost** — the same rule as ASP.NET middleware:

```csharp
Policy
    .Timeout(TimeSpan.FromSeconds(30))   // 1. total budget around everything below
    .Retry(3)                            // 2. retries happen inside that budget
    .CircuitBreaker(5, TimeSpan.FromSeconds(30))
    .Timeout(TimeSpan.FromSeconds(5));   // 4. each individual attempt gets 5s
```

Merge independently defined policies:

```csharp
var breaker  = Policy.CircuitBreaker(5, TimeSpan.FromSeconds(30));   // built once — holds the circuit state
var reads    = Policy.Retry(3).Wrap(breaker);
var writes   = Policy.Timeout(TimeSpan.FromSeconds(5)).Wrap(breaker);
// reads and writes share ONE circuit: failures through either trip both.

var combined = Policy.Compose(timeoutPolicy, retryPolicy, breakerPolicy);  // first = outermost
```

That's the state-sharing rule in one line: **strategy state lives with the policy instance that created it**. Reuse the instance to share a circuit or a rate limiter; build a new one for fresh state.

## Executing

```csharp
await policy.ExecuteAsync(ct => FetchAsync(ct), cancellationToken);          // async
await policy.ExecuteAsync(ct => SaveAsync(ct), cancellationToken);           // async void
policy.Execute(ct => ComputeSync(ct));                                      // sync (same policy!)

// Zero-closure hot path: thread your state instead of capturing it
await policy.ExecuteAsync((client, id), static (s, ct) => s.client.GetUserAsync(s.id, ct), ct);

// No-throw execution: inspect the outcome instead
Outcome<User> outcome = await policy.ExecuteOutcomeAsync(ct => LoadAsync(ct));
if (!outcome.IsSuccess) logger.LogError(outcome.Exception, "gave up");
```

The same policy serves any result type, sync or async. (One exception: hedging is inherently concurrent and requires async execution.)

## Dependency injection

```csharp
services.AddKevlarPolicy("github", Policy.Timeout(TimeSpan.FromSeconds(10)).Retry(3));
services.AddKevlarPolicy<HttpResponseMessage>("downstream",
    sp => HttpKevlar.HandleTransient().Retry(3).WithName("downstream"));

// Consume via the registry…
var policy = registry.GetPolicy("github");                       // IKevlarRegistry
// …or as a keyed service
public sealed class GitHubClient([FromKeyedServices("github")] Policy policy) { }
```

## HTTP

```csharp
services.AddHttpClient("api")
    .AddStandardKevlar();     // 30s total timeout → 3 jittered retries (honouring Retry-After,
                              // disposing retried responses) → circuit breaker → 10s attempt timeout

// Or bring your own:
services.AddHttpClient("api")
    .AddKevlar(HttpKevlar.HandleTransient()      // HttpRequestException, attempt timeouts, 5xx, 408, 429
        .Retry(o => { o.MaxRetries = 4; o.DelayGenerator = HttpKevlar.RetryAfter; })
        .CircuitBreaker(o => o.FailureRatio = 0.5));
```

## Custom strategies

Everything in Kevlar is a `Strategy` — middleware over an `Outcome<T>` pipeline. Write your own:

```csharp
public sealed class LoggingStrategy(ILogger logger) : Strategy
{
    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next, KevlarContext context)
    {
        var start = context.TimeProvider.GetTimestamp();
        var outcome = await next.InvokeAsync(context);
        logger.LogInformation("{Policy} took {Elapsed}", context.PolicyName,
            context.TimeProvider.GetElapsedTime(start));
        return outcome;
    }
}

var policy = Policy.Use(new LoggingStrategy(logger)).Retry(3);
```

Strategies return failures as outcomes rather than throwing, so outer strategies can react to them. Invoke `next` zero times (short-circuit), once (decorate), or many times (retry/hedge).

## Testing your policies

Every delay, timeout and time window runs on a `TimeProvider`:

```csharp
var time = new FakeTimeProvider();   // Microsoft.Extensions.TimeProvider.Testing
var policy = Policy.Retry(3, Backoff.Constant(TimeSpan.FromSeconds(10))).WithTimeProvider(time);

var pending = policy.ExecuteAsync(ct => FlakyAsync(ct)).AsTask();
time.Advance(TimeSpan.FromSeconds(10));                  // no real waiting in tests
```

## Coming from Polly?

| Polly v8 | Kevlar |
|---|---|
| `new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { … }).Build()` | `Policy.Retry(3)` |
| `ShouldHandle = new PredicateBuilder().Handle<T>()` | `Policy.Handle<T>().…` (ambient for the whole chain) |
| `ResiliencePipeline` / `ResiliencePipeline<T>` | `Policy` / `Policy<T>` |
| `ResilienceContextPool.Shared.Get(...)` + `Return` | automatic — contexts are pooled internally |
| `BrokenCircuitException` | `CircuitOpenException` (with `RetryAfter`) |
| `TimeoutRejectedException` | `TimeoutExceededException` |
| `CircuitBreakerManualControl` + `StateProvider` | one `CircuitBreakerMonitor` |
| Retry default: constant 2s, no jitter | exponential + jitter, 30s cap |
| First strategy added is outermost | same rule — pipelines translate 1:1 |

## Performance

Design choices that keep the happy path cheap:

- Failures travel as `Outcome<T>` structs between strategies; exceptions are thrown once, at the boundary, with original stack traces preserved (`ExceptionDispatchInfo`).
- Execution contexts are pooled and recycled automatically.
- State-passing `ExecuteAsync(state, static (s, ct) => …)` overloads make zero-closure call sites easy.
- Strategy chains are prebuilt linked nodes — no per-call graph construction, no LINQ, no boxing of results on the success path.

Early numbers against Polly 8.7 (.NET 10, x64, happy path — see `benchmarks/`):

| Scenario | Kevlar | Polly v8 |
|---|---|---|
| Retry(3), success | **100 ns, 0 B** | 154 ns, 24 B |
| Timeout → Retry → Breaker, success | **272 ns** | 393 ns |

Reproduce with `dotnet run -c Release --project benchmarks/Kevlar.Benchmarks -- --filter '*'`.

## License

MIT

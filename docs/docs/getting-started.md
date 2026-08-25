---
sidebar_position: 2
---

# Getting Started

For failures raised by strategies and satellite packages, see the [exceptions reference](exceptions.md).

## Install

```bash
dotnet add package Kevlar
```

See the canonical [package table](https://github.com/thomhurst/Kevlar#packages) for every optional
integration and testing package.

The core targets `netstandard2.0` (so .NET Framework 4.6.2+ works) and `net10.0`.

## Your first shield

```csharp
using Kevlar;

var shield = Shield.Retry(3);

using var client = new HttpClient();
using var response = await shield.ExecuteAsync(
    ct => client.GetAsync("https://example.com", ct));
```

`Retry(3)` counts retries, not calls: 4 total attempts at most—the initial call plus three retries.
Its default backoff is exponential with equal jitter, starting at 250ms and capped at 30s. This
executable version removes the delays and verifies the count:

<!-- doc-test-run: getting-started-retry-count -->
```csharp
var attempts = 0;
var retryWithoutDelay = Shield.Retry(3, Backoff.None);

try
{
    await retryWithoutDelay.ExecuteAsync(_ =>
    {
        attempts++;
        return ValueTask.FromException(new HttpRequestException("offline"));
    });
}
catch (HttpRequestException)
{
}

if (attempts != 4)
{
    throw new InvalidOperationException($"Expected 4 attempts, observed {attempts}.");
}
```

When strategies are combined, reading order is execution order: the first strategy is the
outermost, like ASP.NET middleware. Put a total timeout before retry and a per-attempt timeout after
it:

```csharp
var productionShield = Shield
    .Timeout(TimeSpan.FromSeconds(30))   // total budget for the whole operation
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
    .Timeout(TimeSpan.FromSeconds(5));   // budget for each attempt
```

This executable check gives the total budget 1.5 seconds and each attempt one second. The first
attempt reaches its inner timeout; the outer timeout then stops the second attempt before a third
can start:

Install the test clock used by this deterministic example:

```bash
dotnet add package Microsoft.Extensions.TimeProvider.Testing
```

<!-- doc-test-run: getting-started-timeout-order -->
```csharp
using Microsoft.Extensions.Time.Testing;

var timeProvider = new FakeTimeProvider();
var attempts = 0;
var secondAttemptStarted = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);
var timedRetry = Shield
    .Timeout(TimeSpan.FromSeconds(1.5))
    .When<TimeoutExceededException>()
    .Retry(2, Backoff.None)
    .Timeout(TimeSpan.FromSeconds(1))
    .WithTimeProvider(timeProvider);

var execution = timedRetry.ExecuteAsync(async token =>
{
    attempts++;
    if (attempts == 2)
    {
        secondAttemptStarted.SetResult();
    }

    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
}).AsTask();

timeProvider.Advance(TimeSpan.FromSeconds(1));
await secondAttemptStarted.Task;
timeProvider.Advance(TimeSpan.FromSeconds(0.5));

try
{
    await execution;
    throw new InvalidOperationException("The total timeout did not fire.");
}
catch (TimeoutExceededException exception) when (exception.Timeout == TimeSpan.FromSeconds(1.5))
{
}

if (attempts != 2)
{
    throw new InvalidOperationException($"Expected 2 attempts, observed {attempts}.");
}
```

Two more things to notice:

1. **Timeout placement changes its scope.** The first timeout wraps all retries; the last timeout
   limits each attempt.
2. **Your delegate gets a cancellation token.** Always use the token you're handed—it's how
   timeouts and hedging cancel abandoned work.

## Reuse it everywhere

Shields are immutable and thread-safe. Build one, store it in a `static readonly` field or register it in [DI](dependency-injection.md), and use it for every call to that dependency:

<!-- doc-test-declaration: split-before=// Any result -->
```csharp
private static readonly Shield GitHubShield = Shield
    .Timeout(TimeSpan.FromSeconds(10))
    .Retry(3);

// Any result type, sync or async, through the same instance:
var repos = await GitHubShield.ExecuteAsync(ct => GetReposAsync(ct), ct);
var user  = await GitHubShield.ExecuteAsync(ct => GetUserAsync(ct), ct);
```

This matters for stateful strategies: a circuit breaker's state lives with the shield instance that created it. Reuse the instance and every call site shares one circuit; build a new instance and you get fresh state. See [Composition](composition.md).

## Deciding what counts as a failure

Reactive strategies (retry, circuit breaker, hedging, fallback) act on failures. By default that
means ordinary exceptions, excluding cancellation, Kevlar's fail-fast rejections, and fatal
runtime failures. Narrow it with a handling clause:

```csharp
var shield = Shield
    .When<HttpRequestException>()
    .Or<TimeoutExceededException>()
    .Retry(5);
```

`When...` returns a `ShieldBuilder`; each `Or...` adds another condition to that immutable builder.
The completed clause is ambient: it applies to `Retry` and every later reactive strategy until a
new clause replaces it, `WhenAnyError()` resets it, or `Wrap`/`Compose` seals it:

```csharp
ShieldBuilder transient = Shield.When<HttpRequestException>();

var protectedCall = transient
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
// Both strategies handle only HttpRequestException.
```

This check proves an unrelated exception does not count toward the inherited breaker threshold:

<!-- doc-test-run: getting-started-ambient-clause -->
```csharp
var ambient = Shield
    .When<HttpRequestException>()
    .Retry(1, Backoff.None)
    .CircuitBreaker(consecutiveFailures: 3, breakDuration: TimeSpan.FromMinutes(1));

try
{
    await ambient.ExecuteAsync(_ => ValueTask.FromException(new ArgumentException("not transient")));
}
catch (ArgumentException)
{
}

try
{
    await ambient.ExecuteAsync(_ => ValueTask.FromException(new HttpRequestException("offline")));
}
catch (HttpRequestException)
{
}

await ambient.ExecuteAsync(_ => ValueTask.CompletedTask);
```

Want to treat certain *results* as failures too (HTTP 500s, say)? Lift into a typed shield with `For<T>`:

```csharp
var http = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode >= 500)
    .Retry(3);
```

Full details in [Handling failures](handling-failures.md).

An untyped `Fallback(...)` still returns `Shield`, but it can recover void executions only. For a
result, start with `Shield.For<TResult>()` and use its typed fallback overloads.

## Next steps

- Browse the [strategy reference](/docs/category/strategies) — each strategy's options, defaults and semantics.
- Wire shields into [dependency injection](dependency-injection.md) or [HttpClient](http.md).
- [Test your shields](testing.md) without real waiting, using `TimeProvider`.

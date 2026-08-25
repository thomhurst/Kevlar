---
sidebar_position: 23
---

# FAQ and troubleshooting

## Why did my shield not retry?

`Retry` reacts only to handled outcomes. The default handling clause includes ordinary exceptions,
but excludes caller cancellation, Kevlar fail-fast rejections, and fatal runtime failures. Result
values are successful unless a typed clause handles them. Use `WhenResult` to start one from
`Shield.For<TResult>()`, or `OrResult` to extend an existing typed clause. Check the [handling
guide](handling-failures.md) and put the clause before the reactive strategies it should control.

This executable example makes only one attempt because cancellation is not a transient failure:

<!-- doc-test-run: faq-cancellation -->
```csharp
var attempts = 0;
var outcome = await Shield
    .Retry(3, Backoff.None)
    .ExecuteOutcomeAsync<int>(_ =>
    {
        attempts++;
        return ValueTask.FromException<int>(new OperationCanceledException());
    });

if (attempts != 1 || outcome.Exception is not OperationCanceledException)
{
    throw new InvalidOperationException("Caller cancellation must not be retried.");
}
```

Remember that `Retry(3)` means one initial attempt plus at most three retries.

## Why did my POST run once or throw `HttpRequestReplayException`?

HTTP retries and hedges need a fresh request per attempt. Method safety and content replayability
are separate requirements. POST, PATCH, and custom methods require `AllowUnsafeMethodReplay = true`
or a `RequestFactory`. Their content must also be replayable: select buffering, supply a
`RequestFactory`, or use content supported by the `NoBuffer` policy. A `HttpRequestReplayException`
indicates a replay configuration failure such as a null request from the factory or content
exceeding the requested buffer limit. See [HTTP replay safety](http.md#safe-request-replay).

Do not enable unsafe replay blindly. Prefer idempotency keys and server-side deduplication for
operations that create or mutate data.

## Why are some metrics missing on .NET 8?

Execution counters and duration histograms are available on .NET 8. State gauges for open circuits,
available permits, and queued work use runtime APIs available in Kevlar's .NET 10 asset. The
complete target matrix is in [observability](observability.md#metrics).

Also verify that your `MeterListener` or OpenTelemetry provider subscribes to the `Kevlar` meter
before executions begin.

## Why does `catch (TimeoutException)` not catch a Kevlar timeout? <!-- doc-lint: allow-TimeoutException -->

Kevlar throws `TimeoutExceededException`; it is deliberately distinct from
`System.TimeoutException` so a resilience timeout cannot be confused with an exception produced by
the protected dependency. Catch the Kevlar type or include it in a handling clause:

<!-- doc-test-ignore: LoadAsync is the application's protected operation. -->
```csharp
try
{
    await Shield.Timeout(TimeSpan.FromSeconds(2))
        .ExecuteAsync(token => LoadAsync(token));
}
catch (TimeoutExceededException exception)
{
    Console.WriteLine($"Budget exceeded after {exception.Timeout}.");
}
```

See the [exception reference](exceptions.md) for base classes, default handling, and public
properties.

## Why did changing an options object not update my shield?

Strategy options are read while the shield is built. Shields are immutable; mutating the original
options object later has no effect. Build a new shield, or use the dependency-injection reload APIs
and resolve a fresh registry/provider snapshot. See [thread safety](thread-safety.md).

## Why did my circuit breaker never open?

Reuse one shield instance. Building a new shield per request creates a new circuit breaker with
empty state. Confirm that the breaker handles the observed exception or result. Consecutive mode
opens after its configured number of consecutive handled failures. Ratio mode opens only after its
failure-ratio and minimum-throughput thresholds are met within the sampling window. The
[circuit-breaker guide](strategies/circuit-breaker.md) covers both modes.

## Still stuck?

Open the repository's question form with the package, Kevlar version, target framework, smallest
shield definition, and observed outcome. For vulnerabilities, use the private
[private security advisory form](https://github.com/thomhurst/Kevlar/security/advisories/new).

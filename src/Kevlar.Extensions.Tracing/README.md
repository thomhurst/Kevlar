# Kevlar.Extensions.Tracing

Add Kevlar strategy events to existing application traces without creating spans.
Supports .NET Standard 2.0, .NET 8, and .NET 10.

```csharp
using Kevlar.Extensions.Tracing;

// Keep this subscription for the application lifetime; dispose it during shutdown.
using var tracing = KevlarTracing.Listen();
```

The adapter enriches the sampled `Activity.Current` at each telemetry callback. It ignores
missing, stopped, propagation-only, and unrecorded activities. Configure tracing and sampling
in your application as usual; no OpenTelemetry SDK dependency is required.

Events contain a fixed schema of strategy, attempt, outcome, duration, delay, circuit, rejection,
and hedge metadata. Exception types are included. Results, bodies, arbitrary context properties,
operation keys, exception messages, and stack traces are excluded by default. String values
are limited to 256 UTF-16 code units. `KevlarTracingOptions` can opt into operation keys and
exception details and change the positive length limit; options are copied at registration.

Multiple registrations independently add duplicate events. Disposal is idempotent; an in-flight
callback can finish after disposal. Async retries and hedges use normal execution-context flow.
If transport spans finish before a retry callback, that callback enriches the enclosing application
activity. Keep an application activity open across the full protected operation.

See the [observability guide](https://thomhurst.github.io/Kevlar/docs/observability)
for a runnable example, the event schema, and integration limits.

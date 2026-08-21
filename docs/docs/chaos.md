---
sidebar_position: 11
---

# Chaos engineering

`Kevlar.Chaos` is an optional package for deliberately injecting latency, exceptions, typed
results, and caller-defined behavior through ordinary shields.

```bash
dotnet add package Kevlar.Chaos
```

Every chaos strategy is disabled by default. Enabling one should be an explicit, reviewable
decision. Bound its blast radius with a low rate plus an operation, environment, predicate, or
dynamic kill switch. Do not broadly enable chaos in production; start in tests or staging, keep
an immediate disable path, monitor injection metrics, and expand gradually.

## Inject latency and faults

```csharp
var latency = ChaosShield.Latency(options =>
{
    options.Enabled = true;
    options.Delay = TimeSpan.FromMilliseconds(250);
    options.InjectionRate = 0.02;
    options.Environment = "staging";
});

var faults = ChaosShield.Fault(options =>
{
    options.Enabled = true;
    options.ExceptionGenerator = context =>
        new IOException($"Injected fault in {context.ShieldName}");
    options.Operation = "checkout";
    options.InjectionRate = 0.01;
});

using (ChaosScope.Begin(operation: "checkout", environment: "staging"))
{
    var result = await Shield.Compose(latency, faults)
        .WithName("payments")
        .ExecuteAsync(token => LoadUserAsync(id, token), cancellationToken);
}
```

Latency honors the shield's `TimeProvider` and caller cancellation token. Fault injection
short-circuits the pipeline with the exact configured exception. If no exception is configured,
`ChaosInjectedException` is used.

## Inject typed outcomes

Typed outcome injection is useful for failures represented as values, such as HTTP responses or
sentinels. It returns a `Shield<TResult>`, so normal typed handling and composition remain available:

```csharp
var unavailable = ChaosShield.Outcome<int>(options =>
{
    options.Enabled = true;
    options.Result = -1;
    options.InjectionRate = 0.05;
});

var shield = Shield.For<int>()
    .WhenResult(-1)
    .Fallback(0)
    .Wrap(unavailable);

var value = await shield.ExecuteAsync(static _ => new ValueTask<int>(42));
```

## Dynamic control and deterministic runs

`Enabled` is the primary safety gate. `EnabledGenerator` can add a live kill switch;
`InjectionRateGenerator` and `Predicate` receive the current `KevlarContext`. Fixed configuration
is copied when the shield is built, while generators are evaluated on every execution.

```csharp
var chaosEnabled = true;
var deterministic = ChaosShield.Fault(options =>
{
    options.Enabled = true;
    options.EnabledGenerator = _ => chaosEnabled;
    options.Predicate = context => !context.IsSynchronous;
    options.InjectionRateGenerator = context =>
        context.ShieldName == "test-run" ? 0.25 : 0;
    options.Seed = 42;
}).WithName("test-run");
```

A seed makes the random sample sequence reproducible. One shield instance is thread-safe and can
be shared, but concurrent callers can consume that deterministic sequence in different orders;
use separate seeded shields when each scenario needs its own exact sequence.

## Custom behavior

`ChaosShield.Behavior` awaits caller-supplied behavior before continuing. It can model effects
that are not just a delay or result, and any exception it throws is preserved as the execution
failure.

```csharp
var behavior = ChaosShield.Behavior(options =>
{
    options.Enabled = true;
    options.Behavior = context =>
    {
        Console.WriteLine($"Injecting custom behavior into {context.ShieldName}");
        return ValueTask.CompletedTask;
    };
});
```

## Observe injections

`OnInjected` receives a `ChaosEvent` immediately before an injection. It identifies the injection
kind, effective rate and sample, operation, environment, and `KevlarContext`. The context is pooled:
copy values inside the callback rather than retaining the context or event.

On .NET 8+, the `Kevlar.Chaos` meter publishes the `kevlar.chaos.injections` counter. Its tags are
`kevlar.chaos.kind` plus the available `kevlar.shield.name`, `kevlar.chaos.operation`, and
`kevlar.chaos.environment` values. Subscribe with OpenTelemetry using
`AddMeter(ChaosDiagnostics.MeterName)`.

For latency tests, attach a `FakeTimeProvider` with `WithTimeProvider` and advance virtual time;
no real waiting is required.

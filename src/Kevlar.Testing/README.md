# Kevlar.Testing

Inspect shield pipelines, assert strategy order, snapshot live state, capture telemetry, and drive
time deterministically without depending on one test framework.

```shell
dotnet add package Kevlar.Testing
```

```csharp
using Kevlar;
using Kevlar.Testing;

var shield = Shield.Timeout(TimeSpan.FromSeconds(10)).Retry(3);

shield.GetDescriptor()
    .AssertStrategyOrder(StrategyKind.Timeout, StrategyKind.Retry)
    .AssertContainsSingle<RetryStrategyDescriptor>();
```

`GetDescriptor()` excludes transparent infrastructure decorators such as structured logging by
default. Pass `includeTransparent: true` to inspect them explicitly.

See the [testing guide](https://thomhurst.github.io/Kevlar/docs/testing) for descriptors,
assertions, state snapshots, execution probes, fake time, and `TelemetryRecorder`.

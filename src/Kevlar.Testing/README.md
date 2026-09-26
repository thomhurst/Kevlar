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

Call `shield.GetDescriptor().Explain()` for a conservative attempt bound, timeout scopes, and
effective handling sources. For example, two nested `Retry(3)` strategies allow at most 16
protected-operation invocations, not six. The explanation distinguishes finite, unbounded,
indeterminate, and overflowing bounds without executing predicates or generators.

This optional inspection API remains in `Kevlar.Testing`; it is suitable for on-demand production
diagnostics as well as tests. It does not instrument execution or add dependencies to `Kevlar`.
Explanation and text formatting allocate, so keep them outside request hot paths.

See the [testing guide](https://thomhurst.github.io/Kevlar/docs/testing) for descriptors,
assertions, state snapshots, execution probes, fake time, and `TelemetryRecorder`.

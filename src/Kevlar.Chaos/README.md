# Kevlar.Chaos

Opt-in chaos strategies for Kevlar inject controlled latency, exceptions, results, or custom
behavior with explicit blast-radius controls.

```shell
dotnet add package Kevlar.Chaos
```

```csharp
using Kevlar.Chaos;

var latency = ChaosShield.Latency(options =>
{
    options.Enabled = true;
    options.Delay = TimeSpan.FromMilliseconds(250);
    options.InjectionRate = 0.02;
    options.Environment = "staging";
});
```

See the [chaos engineering guide](https://thomhurst.github.io/Kevlar/docs/chaos) for deterministic
runs, operation scopes, safety controls, and telemetry.

# Kevlar

Kevlar is an allocation-conscious resilience library for .NET. Compose retries, timeouts, circuit
breakers, hedging, fallbacks, rate limits, and concurrency limits through one immutable `Shield` API.

```shell
dotnet add package Kevlar
```

```csharp
using Kevlar;

var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30));
```

Start with the [Kevlar getting-started guide](https://thomhurst.github.io/Kevlar/docs/getting-started)
or browse the [API reference](https://thomhurst.github.io/Kevlar/api/index.html).

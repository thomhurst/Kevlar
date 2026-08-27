# Kevlar.Extensions.RateLimiting

Adapt `System.Threading.RateLimiting` instances, partitioned limiters, or custom asynchronous lease
acquisition into Kevlar shields.

```shell
dotnet add package Kevlar.Extensions.RateLimiting
```

```csharp
using Kevlar;
using Kevlar.Extensions.RateLimiting;
using System.Threading.RateLimiting;

using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
{
    PermitLimit = 100,
    Window = TimeSpan.FromSeconds(1),
    QueueLimit = 0,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
});

var shield = Shield.Empty.UseRateLimiter(limiter);
```

See the [rate-limiting guide](https://thomhurst.github.io/Kevlar/docs/strategies/rate-limit) for
ownership, rejection metadata, partitioning, and custom lease acquisition.

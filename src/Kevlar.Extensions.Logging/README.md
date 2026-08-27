# Kevlar.Extensions.Logging

Map Kevlar strategy events to stable, structured `Microsoft.Extensions.Logging` events without
replacing strategy callbacks.

```shell
dotnet add package Kevlar.Extensions.Logging
```

```csharp
using Kevlar;
using Kevlar.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

var shield = Shield.Retry(3)
    .WithName("catalog")
    .WithLogging(NullLogger.Instance);
```

See the [structured logging guide](https://thomhurst.github.io/Kevlar/docs/logging) for event IDs,
severity mapping, scopes, throttling, and result formatting.

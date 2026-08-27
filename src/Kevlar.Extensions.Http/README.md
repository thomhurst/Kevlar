# Kevlar.Extensions.Http

Add Kevlar resilience to `HttpClientFactory` with request replay controls, transient-fault
handling, `Retry-After` support, and a production-ready standard pipeline.

```shell
dotnet add package Kevlar.Extensions.Http
```

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpClient("catalog")
    .AddStandardShield();
```

See the [HTTP integration guide](https://thomhurst.github.io/Kevlar/docs/http) for pipeline
defaults, request replay safety, hedging, routing, and configuration reload.

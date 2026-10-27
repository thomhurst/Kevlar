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

See the [HTTP integration guide](https://kevlar.tomlonghurst.dev/docs/http) for pipeline
defaults, request replay safety, hedging, routing, and configuration reload.

By default, GET, HEAD, OPTIONS, TRACE, PUT, and DELETE permit additional attempts.
Use `IsReplaySafeMethod` to narrow this set for APIs whose PUT or DELETE operations
cannot safely be repeated. Selector and partition registrations also accept handler options:

```csharp
using System.Net.Http;
using Kevlar;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
var shield = HttpShield.WhenTransient().Retry(1, Backoff.None);
using var partitions = new PartitionedShield<string, HttpResponseMessage>(_ => shield);
var replayOptions = new ShieldHttpHandlerOptions
{
    IsReplaySafeMethod = static method =>
        method.Method is "GET" or "HEAD" or "OPTIONS" or "TRACE",
};
services.AddHttpClient("selected")
    .AddShield((request, provider) => shield, replayOptions);
services.AddHttpClient("partitioned")
    .AddShield(partitions, request => request.RequestUri!.Host, replayOptions);
```

The predicate replaces the default set; `null` restores the default. It must be thread-safe.
`request.AllowReplay()`, `AllowUnsafeMethodReplay`, and `RequestFactory` explicitly opt in
to additional attempts even when the predicate rejects the method. Content must still be
replayable, or supplied afresh by the factory. `request.DisableReplay()` always wins.
Rejected methods retain the `unsafe_method` suppression reason and its first-use warning.
Options are snapshotted at registration or pipeline creation; replacing a predicate later
does not change an existing pipeline. State captured by the predicate must also be thread-safe.

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

## .NET Framework

For .NET Framework 4.6.2 or later, use an SDK-style project and edit the project file manually;
`dotnet new console` does not accept `-f net48`. The getting-started HTTP sample needs these
settings and an explicit `using System.Net.Http;`:

```xml
<PropertyGroup>
  <TargetFramework>net48</TargetFramework>
  <LangVersion>latest</LangVersion>
</PropertyGroup>

<ItemGroup>
  <Reference Include="System.Net.Http" />
</ItemGroup>
```

Expect MSBuild to generate binding redirects for the transitive compatibility dependencies:
`Microsoft.Bcl.AsyncInterfaces` 8.0.0, `Microsoft.Bcl.TimeProvider` 8.0.1, `Reservoir` 1.4.0,
`System.Runtime.CompilerServices.Unsafe` 6.1.2, `System.Threading.Tasks.Extensions` 4.6.3, and
`System.ValueTuple` 4.5.0.

Start with the [Kevlar getting-started guide](https://thomhurst.github.io/Kevlar/docs/getting-started)
or browse the [API reference](https://thomhurst.github.io/Kevlar/api/index.html).

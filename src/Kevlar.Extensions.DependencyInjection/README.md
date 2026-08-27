# Kevlar.Extensions.DependencyInjection

Register immutable named Kevlar shields with `Microsoft.Extensions.DependencyInjection`, then
resolve them through keyed services or `IKevlarRegistry`.

```shell
dotnet add package Kevlar.Extensions.DependencyInjection
```

```csharp
using Kevlar;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddShield(
    "catalog",
    Shield.Timeout(TimeSpan.FromSeconds(10)).Retry(3));
```

See the [dependency-injection guide](https://thomhurst.github.io/Kevlar/docs/dependency-injection)
for factories, configuration reload, partitioning, and registry behavior.

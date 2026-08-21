using BenchmarkDotNet.Attributes;
using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Benchmarks;

/// <summary>Measures the atomic current-snapshot read against a direct shield reference.</summary>
[MemoryDiagnoser]
public class ReloadingShieldProviderBenchmarks
{
    private ServiceProvider _services = null!;
    private IShieldProvider _provider = null!;
    private Shield _snapshot = null!;

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retry:MaxRetries"] = "1",
                ["Retry:Backoff"] = "None",
            })
            .Build();
        _services = new ServiceCollection()
            .AddReloadingShield("benchmark", configuration)
            .BuildServiceProvider();
        _provider = _services.GetRequiredKeyedService<IShieldProvider>("benchmark");
        _snapshot = _provider.Current;
    }

    [Benchmark(Baseline = true)]
    public Shield DirectSnapshot() => _snapshot;

    [Benchmark]
    public Shield ReloadAwareCurrent() => _provider.Current;

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();
}

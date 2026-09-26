using Kevlar.Extensions.DependencyInjection;
using Kevlar.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class CircuitBreakerProbeConfigurationTests
{
    [Test]
    [Arguments(0, 1, 0.5, true)]
    [Arguments(-1, 1, 0.5, true)]
    [Arguments(1, 0, 0.5, true)]
    [Arguments(1, -1, 0.5, true)]
    [Arguments(1, 1, 0, true)]
    [Arguments(1, 1, -0.5, true)]
    [Arguments(1, 1, 1.1, true)]
    [Arguments(1, 1, double.NaN, true)]
    [Arguments(1, 1, double.PositiveInfinity, true)]
    [Arguments(1, null, 0.5, true)]
    [Arguments(1, 1, null, true)]
    [Arguments(1, 1, 0.5, false)]
    public async Task Invalid_Probe_And_Slow_Call_Options_Are_Rejected_For_Both_Builders(
        int probes, int? thresholdMilliseconds, double? slowRatio, bool ratioMode)
    {
        var threshold = thresholdMilliseconds is { } milliseconds
            ? TimeSpan.FromMilliseconds(milliseconds)
            : (TimeSpan?)null;
        await Assert.That(() => Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = ratioMode ? 0.5 : null;
            options.HalfOpenProbes = probes;
            options.SlowCallThreshold = threshold;
            options.SlowCallRatio = slowRatio;
        })).Throws<KevlarConfigurationException>();
        await Assert.That(() => Shield.For<int>().CircuitBreaker(options =>
        {
            options.FailureRatio = ratioMode ? 0.5 : null;
            options.HalfOpenProbes = probes;
            options.SlowCallThreshold = threshold;
            options.SlowCallRatio = slowRatio;
        })).Throws<KevlarConfigurationException>();
    }

    [Test]
    public async Task Configuration_Binds_Probe_And_Slow_Call_Settings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CircuitBreaker:FailureRatio"] = "0.5",
            ["CircuitBreaker:HalfOpenProbes"] = "4",
            ["CircuitBreaker:SlowCallThreshold"] = "00:00:00.250",
            ["CircuitBreaker:SlowCallRatio"] = "0.75",
        }).Build();
        var services = new ServiceCollection();
        services.AddShield("untyped", configuration);
        services.AddShield<int>("typed", configuration);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();
        var descriptors = new[]
        {
            registry.GetShield("untyped").GetDescriptor(),
            registry.GetShield<int>("typed").GetDescriptor(),
        };
        foreach (var descriptor in descriptors)
        {
            var breaker = (CircuitBreakerStrategyDescriptor)descriptor.Strategies.Single();
            await Assert.That(breaker.HalfOpenProbes).IsEqualTo(4);
            await Assert.That(breaker.SlowCallThreshold).IsEqualTo(TimeSpan.FromMilliseconds(250));
            await Assert.That(breaker.SlowCallRatio).IsEqualTo(0.75);
            await Assert.That(breaker.Description).Contains("probes 4, slow >250ms ratio 75%");
        }
    }

    [Test]
    public async Task Typed_Options_And_Definitions_Preserve_Descriptor_Settings()
    {
        var typed = Shield.For<int>().CircuitBreaker(options =>
        {
            options.FailureRatio = 0.5;
            options.HalfOpenProbes = 3;
            options.SlowCallThreshold = TimeSpan.FromMilliseconds(10);
            options.SlowCallRatio = 0.8;
        });
        var definition = new ShieldDefinition
        {
            CircuitBreaker = new CircuitBreakerDefinition
            {
                FailureRatio = 0.5,
                HalfOpenProbes = 3,
                SlowCallThreshold = TimeSpan.FromMilliseconds(10),
                SlowCallRatio = 0.8,
            },
        };
        foreach (var descriptor in new[] { typed.GetDescriptor(), definition.Build().GetDescriptor() })
        {
            var breaker = (CircuitBreakerStrategyDescriptor)descriptor.Strategies.Single();
            await Assert.That(breaker.HalfOpenProbes).IsEqualTo(3);
            await Assert.That(breaker.SlowCallThreshold).IsEqualTo(TimeSpan.FromMilliseconds(10));
            await Assert.That(breaker.SlowCallRatio).IsEqualTo(0.8);
        }
    }
}

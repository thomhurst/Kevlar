using Kevlar.Chaos;
using Kevlar.Extensions.Grpc;
using Kevlar.Extensions.Http;

namespace Kevlar.NetStandard.Tests;

public class CompatibilityTargetTests
{
    [Test]
    public async Task Compatibility_Suite_Loads_NetStandard20_Assemblies()
    {
        var assemblies = new[]
        {
            typeof(Shield).Assembly,
            typeof(ShieldDelegatingHandler).Assembly,
            typeof(ShieldStreamingClientInterceptor).Assembly,
            typeof(ChaosShield).Assembly,
        };

        foreach (var assembly in assemblies)
        {
            var target = (System.Runtime.Versioning.TargetFrameworkAttribute)assembly
                .GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
                .Single();
            await Assert.That(target.FrameworkName).IsEqualTo(".NETStandard,Version=v2.0");
        }
    }
}

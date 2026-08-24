using Kevlar.Chaos;
using Kevlar.Extensions.Grpc;
using Kevlar.Extensions.Http;

namespace Kevlar.NetStandard.Tests;

public class CompatibilityTargetTests
{
    [Test]
    public async Task Compatibility_Suite_Loads_Configured_NetStandard_Assemblies()
    {
        var targets = new[]
        {
            (Assembly: typeof(Shield).Assembly, Framework: ".NETStandard,Version=v2.0"),
            (Assembly: typeof(ShieldDelegatingHandler).Assembly, Framework: ".NETStandard,Version=v2.0"),
            (Assembly: typeof(ShieldStreamingClientInterceptor).Assembly, Framework: ".NETStandard,Version=v2.1"),
            (Assembly: typeof(ChaosShield).Assembly, Framework: ".NETStandard,Version=v2.0"),
        };

        foreach (var (assembly, framework) in targets)
        {
            var target = (System.Runtime.Versioning.TargetFrameworkAttribute)assembly
                .GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
                .Single();
            await Assert.That(target.FrameworkName).IsEqualTo(framework);
        }
    }
}

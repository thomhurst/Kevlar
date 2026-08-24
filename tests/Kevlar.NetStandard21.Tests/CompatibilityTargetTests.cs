using Kevlar.Extensions.Grpc;

namespace Kevlar.NetStandard21.Tests;

public class CompatibilityTargetTests
{
    [Test]
    public async Task Compatibility_Suite_Loads_The_NetStandard21_Grpc_Assembly()
    {
        var assembly = typeof(ShieldStreamingClientInterceptor).Assembly;
        var target = (System.Runtime.Versioning.TargetFrameworkAttribute)assembly
            .GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
            .Single();

        await Assert.That(target.FrameworkName).IsEqualTo(".NETStandard,Version=v2.1");
    }
}

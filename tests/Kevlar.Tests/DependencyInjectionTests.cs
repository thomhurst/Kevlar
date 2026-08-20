using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class DependencyInjectionTests
{
    private sealed record TimeoutSetting(TimeSpan Value);

    [Test]
    public async Task Registry_Resolves_Named_Policies()
    {
        var services = new ServiceCollection()
            .AddKevlarPolicy("basic", Policy.Retry(2, Backoff.None))
            .BuildServiceProvider();

        var registry = services.GetRequiredService<IKevlarRegistry>();
        var policy = registry.GetPolicy("basic");

        var result = await policy.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task Typed_And_Untyped_Policies_With_The_Same_Name_Coexist()
    {
        var services = new ServiceCollection()
            .AddKevlarPolicy("shared", Policy.Retry(1, Backoff.None))
            .AddKevlarPolicy("shared", Policy.For<int>().HandleResult(-1).Fallback(0))
            .BuildServiceProvider();

        var registry = services.GetRequiredService<IKevlarRegistry>();

        var untyped = registry.GetPolicy("shared");
        var typed = registry.GetPolicy<int>("shared");

        await Assert.That(untyped).IsNotNull();
        var result = await typed.ExecuteAsync(_ => new ValueTask<int>(-1));
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Policies_Resolve_As_Keyed_Services()
    {
        var services = new ServiceCollection()
            .AddKevlarPolicy("keyed", Policy.Timeout(TimeSpan.FromSeconds(5)))
            .BuildServiceProvider();

        var policy = services.GetRequiredKeyedService<Policy>("keyed");

        var result = await policy.ExecuteAsync(_ => new ValueTask<string>("via-key"));
        await Assert.That(result).IsEqualTo("via-key");
    }

    [Test]
    public async Task Missing_Policy_Throws_A_Helpful_Error()
    {
        var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();

        await Assert.That(() => registry.GetPolicy("missing")).Throws<KeyNotFoundException>();
        await Assert.That(registry.TryGetPolicy("missing", out _)).IsFalse();
    }

    [Test]
    public async Task Factory_Registrations_Receive_The_Service_Provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TimeoutSetting(TimeSpan.FromSeconds(9)));
        services.AddKevlarPolicy("factory", sp => Policy.Timeout(sp.GetRequiredService<TimeoutSetting>().Value));

        var registry = services.BuildServiceProvider().GetRequiredService<IKevlarRegistry>();
        var policy = registry.GetPolicy("factory");

        await Assert.That(policy).IsNotNull();
    }
}

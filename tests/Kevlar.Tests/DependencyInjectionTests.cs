using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class DependencyInjectionTests
{
    [Test]
    public async Task Fallback_Shields_Register_As_Ordinary_Shields()
    {
        var fallbackCalls = 0;
        var services = new ServiceCollection()
            .AddShield("shared", Shield.Retry(1))
            .AddShield("shared", Shield.Fallback(_ =>
            {
                fallbackCalls++;
                return ValueTask.CompletedTask;
            }));
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();
        var fallbackShield = registry.GetShield("shared");

        await fallbackShield.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(registry.GetShield("shared")).IsTypeOf<Shield>();
        await Assert.That(fallbackShield).IsTypeOf<Shield>();
        await Assert.That(provider.GetRequiredKeyedService<Shield>("shared"))
            .IsSameReferenceAs(fallbackShield);
        await Assert.That(fallbackCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Partitioned_Fallback_Shields_Resolve_As_Keyed_Singletons()
    {
        var services = new ServiceCollection()
            .AddPartitionedShield<string>(
                "void-tenants",
                (_, _) => Shield.Fallback(static _ => ValueTask.CompletedTask));
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredKeyedService<PartitionedShield<string>>("void-tenants");
        var second = provider.GetRequiredKeyedService<PartitionedShield<string>>("void-tenants");

        await Assert.That(first).IsSameReferenceAs(second);
        await Assert.That(first.GetShield("acme")).IsSameReferenceAs(second.GetShield("acme"));
    }

    private sealed record TimeoutSetting(TimeSpan Value);

    [Test]
    public async Task Registry_Resolves_Named_Policies()
    {
        var services = new ServiceCollection()
            .AddShield("basic", Shield.Retry(2, Backoff.None))
            .BuildServiceProvider();

        var registry = services.GetRequiredService<IKevlarRegistry>();
        var shield = registry.GetShield("basic");

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task Typed_And_Untyped_Policies_With_The_Same_Name_Coexist()
    {
        var services = new ServiceCollection()
            .AddShield("shared", Shield.Retry(1, Backoff.None))
            .AddShield("shared", Shield.For<int>().WhenResult(-1).FallbackTo(0))
            .BuildServiceProvider();

        var registry = services.GetRequiredService<IKevlarRegistry>();

        var untyped = registry.GetShield("shared");
        var typed = registry.GetShield<int>("shared");

        await Assert.That(untyped).IsNotNull();
        var result = await typed.ExecuteAsync(_ => new ValueTask<int>(-1));
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Policies_Resolve_As_Keyed_Services()
    {
        var services = new ServiceCollection()
            .AddShield("keyed", Shield.Timeout(TimeSpan.FromSeconds(5)))
            .BuildServiceProvider();

        var shield = services.GetRequiredKeyedService<Shield>("keyed");

        var result = await shield.ExecuteAsync(_ => new ValueTask<string>("via-key"));
        await Assert.That(result).IsEqualTo("via-key");
    }

    [Test]
    public async Task Missing_Policy_Throws_A_Helpful_Error()
    {
        var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();

        await Assert.That(() => registry.GetShield("missing")).Throws<KeyNotFoundException>();
        await Assert.That(registry.TryGetShield("missing", out _)).IsFalse();
    }

    [Test]
    public async Task Factory_Registrations_Receive_The_Service_Provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TimeoutSetting(TimeSpan.FromSeconds(9)));
        services.AddShield("factory", sp => Shield.Timeout(sp.GetRequiredService<TimeoutSetting>().Value));

        var registry = services.BuildServiceProvider().GetRequiredService<IKevlarRegistry>();
        var shield = registry.GetShield("factory");

        await Assert.That(shield).IsNotNull();
    }

    [Test]
    public async Task Partitioned_Shields_Resolve_As_Keyed_Singletons()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TimeoutSetting(TimeSpan.FromSeconds(9)));
        services.AddPartitionedShield<string>(
            "tenants",
            (serviceProvider, _) => Shield.Timeout(
                serviceProvider.GetRequiredService<TimeoutSetting>().Value),
            options => options.MaximumPartitions = 10);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredKeyedService<PartitionedShield<string>>("tenants");
        var second = provider.GetRequiredKeyedService<PartitionedShield<string>>("tenants");

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(ReferenceEquals(
            first.GetShield("acme"),
            second.GetShield("acme"))).IsTrue();
    }

    [Test]
    public async Task Typed_And_Untyped_Partitioned_Shields_Coexist()
    {
        var services = new ServiceCollection()
            .AddPartitionedShield<string>("shared", (_, _) => Shield.Empty)
            .AddPartitionedShield<string, int>(
                "shared",
                (_, _) => Shield.For<int>().WhenResult(-1).FallbackTo(0));
        using var provider = services.BuildServiceProvider();

        var untyped = provider.GetRequiredKeyedService<PartitionedShield<string>>("shared");
        var typed = provider.GetRequiredKeyedService<PartitionedShield<string, int>>("shared");
        var result = await typed.GetShield("tenant")
            .ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(untyped.GetShield("tenant")).IsNotNull();
        await Assert.That(result).IsEqualTo(0);
    }
}

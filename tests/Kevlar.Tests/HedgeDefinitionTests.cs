using System.Text;
using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class HedgeDefinitionTests
{
    [Test]
    public async Task Json_Binding_Preserves_The_Documented_Order_For_Typed_And_Untyped_Shields()
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "Timeout": "00:00:30",
              "Hedge": { "MaxHedgedAttempts": 2, "Delay": "00:00:00.100" },
              "CircuitBreaker": { "ConsecutiveFailures": 3, "BreakDuration": "00:00:04" },
              "RateLimit": { "Permits": 5, "Window": "00:00:10", "Burst": 7, "QueueLimit": 2 },
              "ConcurrencyLimit": { "MaxConcurrency": 3, "QueueLimit": 4 },
              "AttemptTimeout": "00:00:01"
            }
            """));
        using var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        using var services = new ServiceCollection()
            .AddShield("untyped", configuration)
            .AddShield<int>("typed", configuration)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        const string pipeline = "Timeout(30s) → Hedge(2 extra, delay 100ms) → " +
            "CircuitBreaker(3 consecutive, break 4s) → RateLimit(5/10s, burst 7, queue 2) → " +
            "ConcurrencyLimit(3, queue 4) → Timeout(1s)";

        await Assert.That(registry.GetShield("untyped").ToString()).IsEqualTo($"untyped: {pipeline}");
        await Assert.That(registry.GetShield<int>("typed").ToString()).IsEqualTo($"typed: {pipeline}");
    }

    [Test]
    public async Task Definition_Defaults_Match_The_Fluent_Api()
    {
        var definition = new ShieldDefinition { Hedge = new HedgeDefinition() };

        await Assert.That(definition.Build().ToString()).IsEqualTo("Hedge(1 extra, delay 1s)");
        await Assert.That(new ShieldDefinition().Build().ToString()).IsEqualTo(Shield.Empty.ToString());
    }

    [Test]
    [Arguments("MaxHedgedAttempts", "2", "Hedge(2 extra, delay 1s)")]
    [Arguments("Delay", "00:00:00.100", "Hedge(1 extra, delay 100ms)")]
    [Arguments("Delay", "00:00:00", "Hedge(1 extra, delay 0s)")]
    [Arguments("Delay", "-00:00:01", "Hedge(1 extra, delay infinite)")]
    public async Task Partial_Configuration_Preserves_Defaults_And_Delay_Semantics(
        string key, string value, string expected)
    {
        using var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [$"Hedge:{key}"] = value }).Build();
        using var services = new ServiceCollection().AddShield("hedge", configuration).BuildServiceProvider();

        await Assert.That(services.GetRequiredService<IKevlarRegistry>().GetShield("hedge").ToString())
            .IsEqualTo($"hedge: {expected}");
    }

    [Test]
    public async Task Retry_And_Hedge_Are_Rejected_Before_Building_Other_Strategies()
    {
        var definition = new ShieldDefinition
        {
            Timeout = TimeSpan.Zero,
            Retry = new RetryDefinition(),
            Hedge = new HedgeDefinition(),
        };

        var error = await Assert.That(() => definition.Build()).Throws<KevlarConfigurationException>();

        await Assert.That(error!.Message).Contains("ShieldDefinition.Retry and ShieldDefinition.Hedge");
    }

    [Test]
    [Arguments("Hedge:MaxHedgedAttempts", "-1")]
    [Arguments("Hedge:MaxHedgedAttempts", "invalid")]
    [Arguments("Hedge:Delay", "invalid")]
    [Arguments("Retry:MaxRetries", "1")]
    public async Task Invalid_Configuration_Reports_The_Path_For_Both_Registration_Forms(string key, string value)
    {
        using var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Resilience:Hedge:Delay"] = "00:00:01",
                [$"Resilience:{key}"] = value,
            }).Build();
        using var services = new ServiceCollection()
            .AddShield("untyped", configuration.GetSection("Resilience"))
            .AddShield<int>("typed", configuration.GetSection("Resilience"))
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();

        var untypedError = await Assert.That(() => registry.GetShield("untyped")).Throws<KevlarConfigurationException>();
        var typedError = await Assert.That(() => registry.GetShield<int>("typed")).Throws<KevlarConfigurationException>();

        await Assert.That(untypedError!.Message).Contains("Resilience");
        await Assert.That(typedError!.Message).Contains("Resilience");
        await Assert.That(untypedError.InnerException).IsTypeOf<KevlarConfigurationException>();
        await Assert.That(typedError.InnerException).IsTypeOf<KevlarConfigurationException>();
    }

    [Test]
    public async Task Reload_Updates_Hedge_Delay_And_Retains_Last_Good_Publication_On_Conflict()
    {
        using var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Hedge:Delay"] = "00:00:01" }).Build();
        var options = new ReloadingShieldOptions { DebounceDelay = TimeSpan.Zero };
        var failures = new List<Exception>();
        using var services = new ServiceCollection()
            .AddReloadingShield("untyped", options, configuration, failures.Add)
            .AddReloadingShield<int>("typed", options, configuration, failures.Add)
            .BuildServiceProvider();
        var untyped = services.GetRequiredKeyedService<IShieldProvider>("untyped");
        var typed = services.GetRequiredKeyedService<IShieldProvider<int>>("typed");
        var firstUntyped = untyped.Current;
        var firstTyped = typed.Current;

        configuration["Hedge:Delay"] = "00:00:00.250";
        configuration.Reload();

        var secondUntyped = untyped.Current;
        var secondTyped = typed.Current;
        await Assert.That(secondUntyped.ToString()).IsEqualTo("untyped: Hedge(1 extra, delay 250ms)");
        await Assert.That(secondTyped.ToString()).IsEqualTo("typed: Hedge(1 extra, delay 250ms)");
        await Assert.That(firstUntyped.ToString()).IsEqualTo("untyped: Hedge(1 extra, delay 1s)");
        await Assert.That(firstTyped.ToString()).IsEqualTo("typed: Hedge(1 extra, delay 1s)");

        configuration["Retry:MaxRetries"] = "1";
        configuration.Reload();

        await Assert.That(untyped.Current).IsSameReferenceAs(secondUntyped);
        await Assert.That(typed.Current).IsSameReferenceAs(secondTyped);
        await Assert.That(failures.Count).IsEqualTo(2);
        foreach (var failure in failures)
        {
            await Assert.That(failure).IsTypeOf<KevlarConfigurationException>();
            await Assert.That(failure.Message).Contains("ShieldDefinition.Retry and ShieldDefinition.Hedge");
        }
    }
}

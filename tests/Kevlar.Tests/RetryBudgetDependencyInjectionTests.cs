using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class RetryBudgetDependencyInjectionTests
{
    [Test]
    public async Task Named_Budget_Is_Shared_By_Configured_Typed_And_Untyped_Shields()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Retry:Budget"] = "downstream",
            ["Retry:MaxRetries"] = "10",
            ["Retry:Backoff"] = "None",
        }).Build();
        var services = new ServiceCollection();
        services.AddRetryBudget("downstream", maxTokens: 4, tokenRatio: 1);
        services.AddShield("first", configuration);
        services.AddShield<int>("second", configuration);
        using var provider = services.BuildServiceProvider();
        var budget = provider.GetRequiredKeyedService<RetryBudget>("downstream");
        var first = provider.GetRequiredKeyedService<Shield>("first");
        var second = provider.GetRequiredKeyedService<Shield<int>>("second");
        _ = await first.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        await Assert.That(budget.Tokens).IsEqualTo(2);
        await Assert.That(second.Execute(static _ => 42)).IsEqualTo(42);
        await Assert.That(budget.Tokens).IsEqualTo(3);
    }

    [Test]
    public async Task Definition_Resolves_Hedge_Budget_And_Reports_Missing_Registration()
    {
        var definition = new ShieldDefinition
        {
            Hedge = new HedgeDefinition { Budget = "downstream", MaxHedgedAttempts = 0 },
        };
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        var services = new ServiceCollection().AddRetryBudget("downstream", budget);
        using var provider = services.BuildServiceProvider();
        var shield = definition.Build(provider);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        await Assert.That(budget.Tokens).IsEqualTo(3);
        await Assert.That(() => definition.Build()).Throws<KevlarConfigurationException>();
        using var missing = new ServiceCollection().BuildServiceProvider();
        await Assert.That(() => definition.Build(missing)).Throws<KevlarConfigurationException>();
        await Assert.That(() => services.AddRetryBudget("downstream", budget)).Throws<InvalidOperationException>();
    }
}

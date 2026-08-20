using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

/// <summary>
/// Guards configuration-bound shields: <see cref="ShieldDefinition"/> builds the declared
/// pipeline in the documented order, and <c>AddShield(name, IConfiguration)</c> registers a
/// working, named shield whose knobs come from configuration.
/// </summary>
public class ConfigurationBindingTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    [Test]
    public async Task A_Definition_Builds_The_Declared_Pipeline_In_Order()
    {
        var definition = new ShieldDefinition
        {
            Timeout = TimeSpan.FromSeconds(30),
            Retry = new RetryDefinition { MaxRetries = 4, Backoff = BackoffKind.None },
            CircuitBreaker = new CircuitBreakerDefinition { ConsecutiveFailures = 5, BreakDuration = TimeSpan.FromSeconds(30) },
            ConcurrencyLimit = new ConcurrencyLimitDefinition { MaxConcurrency = 8 },
            AttemptTimeout = TimeSpan.FromSeconds(5),
        };

        await Assert.That(definition.Build().ToString())
            .IsEqualTo("Timeout(30s) → Retry(4, no delay) → CircuitBreaker(5 consecutive, break 30s) → ConcurrencyLimit(8) → Timeout(5s)");
    }

    [Test]
    public async Task An_Empty_Definition_Builds_An_Empty_Shield()
    {
        await Assert.That(new ShieldDefinition().Build().ToString()).IsEqualTo("(empty)");
    }

    [Test]
    public async Task A_Config_Bound_Shield_Is_Registered_And_Works()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "2"),
            ("Retry:Backoff", "None"));

        var services = new ServiceCollection();
        services.AddShield("github", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("github");
        await Assert.That(shield.Name).IsEqualTo("github");

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException();
            }

            return new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Configuration_Sets_Backoff_And_Breaker_Knobs()
    {
        var configuration = BuildConfiguration(
            ("Timeout", "00:00:30"),
            ("Retry:MaxRetries", "5"),
            ("Retry:Backoff", "Constant"),
            ("Retry:BaseDelay", "00:00:01"),
            ("CircuitBreaker:FailureRatio", "0.5"),
            ("CircuitBreaker:MinimumThroughput", "20"),
            ("CircuitBreaker:SamplingWindow", "00:00:30"),
            ("CircuitBreaker:BreakDuration", "00:00:15"));

        var services = new ServiceCollection();
        services.AddShield("tuned", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("tuned");

        await Assert.That(shield.ToString())
            .IsEqualTo("tuned: Timeout(30s) → Retry(5, constant 1s) → CircuitBreaker(50% over 30s, min 20, break 15s)");
    }

    [Test]
    public async Task Exponential_Defaults_Match_The_Fluent_Api()
    {
        var definition = new ShieldDefinition { Retry = new RetryDefinition() };

        await Assert.That(definition.Build().ToString())
            .IsEqualTo("Retry(3, exponential 250ms ×2 +jitter ≤30s)");
    }
}

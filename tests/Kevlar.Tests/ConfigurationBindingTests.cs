using System.Globalization;
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
            .IsEqualTo("tuned: Timeout(30s) → Retry(5, constant 1s, equal jitter) → CircuitBreaker(50% over 30s, min 20, break 15s)");
    }

    [Test]
    [Arguments("none", "no delay")]
    [Arguments("CONSTANT", "constant 1s")]
    [Arguments("Linear", "linear 1s steps")]
    [Arguments("eXpOnEnTiAl", "exponential 1s ×2, cap 30s")]
    public async Task RetryDefinition_Binds_BackoffKind_From_Configuration(string value, string expected)
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "1"),
            ("Retry:Backoff", value),
            ("Retry:BaseDelay", "00:00:01"),
            ("Retry:Jitter", "None"),
            ("Retry:MaxDelay", ""));
        var services = new ServiceCollection();
        services.AddShield("backoff", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("backoff");

        await Assert.That(shield.ToString()).IsEqualTo($"backoff: Retry(1, {expected})");
    }

    [Test]
    public async Task Invalid_BackoffKind_Throws_With_Path()
    {
        var configuration = BuildConfiguration(("Retry:Backoff", "quadratic"));
        var services = new ServiceCollection();
        services.AddShield("invalid", configuration);
        using var provider = services.BuildServiceProvider();

        await Assert.That(() => provider.GetRequiredService<IKevlarRegistry>().GetShield("invalid"))
            .Throws<InvalidOperationException>()
            .WithMessage("Configuration value 'quadratic' for 'Retry:Backoff' is not a BackoffKind.");
    }

    [Test]
    [Arguments("None", "exponential 1s ×2, cap 30s")]
    [Arguments("equal", "exponential 1s ×2, equal jitter, cap 30s")]
    [Arguments("FULL", "exponential 1s ×2, full jitter, cap 30s")]
    [Arguments("Decorrelated", "exponential 1s ×2, decorrelated jitter, cap 30s")]
    [Arguments("false", "exponential 1s ×2, cap 30s")]
    [Arguments("true", "exponential 1s ×2, equal jitter, cap 30s")]
    public async Task RetryDefinition_Binds_Jitter_From_Configuration(string value, string expected)
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "1"),
            ("Retry:BaseDelay", "00:00:01"),
            ("Retry:Jitter", value));
        var services = new ServiceCollection();
        services.AddShield("jitter", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("jitter");

        await Assert.That(shield.ToString()).IsEqualTo($"jitter: Retry(1, {expected})");
    }

    [Test]
    public async Task Invalid_Jitter_Throws_With_Path()
    {
        var configuration = BuildConfiguration(("Retry:Jitter", "randomish"));
        var services = new ServiceCollection();
        services.AddShield("invalid", configuration);
        using var provider = services.BuildServiceProvider();

        await Assert.That(() => provider.GetRequiredService<IKevlarRegistry>().GetShield("invalid"))
            .Throws<InvalidOperationException>()
            .WithMessage("Configuration value 'randomish' for 'Retry:Jitter' is not a Jitter.");
    }

    [Test]
    public async Task Custom_BackoffKind_Throws_With_Path()
    {
        var configuration = BuildConfiguration(("Retry:Backoff", "Custom"));
        var services = new ServiceCollection();
        services.AddShield("invalid", configuration);
        using var provider = services.BuildServiceProvider();

        await Assert.That(() => provider.GetRequiredService<IKevlarRegistry>().GetShield("invalid"))
            .Throws<InvalidOperationException>()
            .WithMessage(
                "Configuration value 'Custom' for 'Retry:Backoff' is not a configurable "
                + "BackoffKind (None, Constant, Linear, or Exponential).");
    }

    [Test]
    public async Task Exponential_Defaults_Match_The_Fluent_Api()
    {
        var definition = new ShieldDefinition { Retry = new RetryDefinition() };

        await Assert.That(definition.Build().ToString())
            .IsEqualTo("Retry(3, exponential 250ms ×2, equal jitter, cap 30s)");
    }

    [Test]
    public async Task Configuration_Binds_Every_Strategy_Option_Without_Reflection()
    {
        var configuration = BuildConfiguration(
            ("Timeout", "00:00:20"),
            ("Retry:MaxRetries", "2"),
            ("Retry:Backoff", "exponential"),
            ("Retry:BaseDelay", "00:00:00.100"),
            ("Retry:Factor", "3"),
            ("Retry:Jitter", "None"),
            ("Retry:MaxDelay", "00:00:02"),
            ("CircuitBreaker:FailureRatio", "0.25"),
            ("CircuitBreaker:MinimumThroughput", "4"),
            ("CircuitBreaker:SamplingWindow", "00:00:08"),
            ("CircuitBreaker:BreakDuration", "00:00:05"),
            ("RateLimit:Permits", "5"),
            ("RateLimit:Window", "00:00:10"),
            ("RateLimit:Burst", "7"),
            ("RateLimit:QueueLimit", "2"),
            ("ConcurrencyLimit:MaxConcurrency", "3"),
            ("ConcurrencyLimit:QueueLimit", "4"),
            ("AttemptTimeout", "00:00:01"));
        var services = new ServiceCollection();
        services.AddShield("complete", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("complete");

        await Assert.That(shield.ToString()).IsEqualTo(
            "complete: Timeout(20s) → Retry(2, exponential 100ms ×3, cap 2s) → CircuitBreaker(25% over 8s, min 4, break 5s) → RateLimit(5/10s, burst 7, queue 2) → ConcurrencyLimit(3, queue 4) → Timeout(1s)");
    }

    [Test]
    public async Task ConcurrencyLimit_Definition_Binds_QueueLimit_From_Configuration()
    {
        var configuration = BuildConfiguration(
            ("ConcurrencyLimit:MaxConcurrency", "3"),
            ("ConcurrencyLimit:QueueLimit", "5"));
        var services = new ServiceCollection();
        services.AddShield("queue", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("queue");

        await Assert.That(shield.ToString()).IsEqualTo("queue: ConcurrencyLimit(3, queue 5)");
    }

    [Test]
    public async Task Unknown_Key_MaxQueue_Is_Rejected_With_Configuration_Path()
    {
        var configuration = BuildConfiguration(("ConcurrencyLimit:MaxQueue", "5"));
        var services = new ServiceCollection();
        services.AddShield("legacy", configuration);
        using var provider = services.BuildServiceProvider();

        var exception = await Assert.That(
                () => provider.GetRequiredService<IKevlarRegistry>().GetShield("legacy"))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("ConcurrencyLimit:MaxQueue");
        await Assert.That(exception.Message).Contains("QueueLimit");
    }

    [Test]
    public async Task Configuration_Preserves_Definition_Defaults_For_Absent_Keys()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "3"),
            ("CircuitBreaker:FailureRatio", "0.5"),
            ("RateLimit:Burst", "100"),
            ("ConcurrencyLimit:QueueLimit", "0"));
        var services = new ServiceCollection();
        services.AddShield("defaults", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("defaults");

        await Assert.That(shield.ToString()).IsEqualTo(
            "defaults: Retry(3, exponential 250ms ×2, equal jitter, cap 30s) → CircuitBreaker(50% over 30s, min 10, break 15s) → RateLimit(100/1s) → ConcurrencyLimit(10)");
    }

    [Test]
    public async Task Configuration_Parses_Numbers_Using_Invariant_Culture()
    {
        var configuration = BuildConfiguration(("CircuitBreaker:FailureRatio", "0.25"));
        var services = new ServiceCollection();
        services.AddShield("invariant", configuration);
        using var provider = services.BuildServiceProvider();
        var previousCulture = CultureInfo.CurrentCulture;
        Shield shield;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("invariant");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        await Assert.That(shield.ToString())
            .IsEqualTo("invariant: CircuitBreaker(25% over 30s, min 10, break 15s)");
    }

    [Test]
    public async Task Configuration_Invalid_Value_Identifies_The_Full_Key()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "abc"));
        var services = new ServiceCollection();
        services.AddShield("invalid", configuration);
        using var provider = services.BuildServiceProvider();

        await Assert.That(() => provider.GetRequiredService<IKevlarRegistry>().GetShield("invalid"))
            .Throws<InvalidOperationException>()
            .WithMessage("Configuration value 'abc' for 'Retry:MaxRetries' is not an integer.");
    }

    [Test]
    public async Task Configuration_Rejects_Explicit_Empty_Value()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", ""));
        var services = new ServiceCollection();
        services.AddShield("empty", configuration);
        using var provider = services.BuildServiceProvider();

        await Assert.That(() => provider.GetRequiredService<IKevlarRegistry>().GetShield("empty"))
            .Throws<InvalidOperationException>()
            .WithMessage("Configuration value '' for 'Retry:MaxRetries' is not an integer.");
    }

    [Test]
    public async Task Configuration_Treats_Empty_Nullable_Values_As_Null()
    {
        var configuration = BuildConfiguration(
            ("Timeout", ""),
            ("AttemptTimeout", ""),
            ("Retry:MaxRetries", "0"),
            ("Retry:BaseDelay", ""),
            ("Retry:MaxDelay", ""),
            ("CircuitBreaker:ConsecutiveFailures", ""),
            ("CircuitBreaker:FailureRatio", "0.5"),
            ("RateLimit:Permits", "5"),
            ("RateLimit:Burst", ""));
        var services = new ServiceCollection();
        services.AddShield("nullable", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("nullable");

        await Assert.That(shield.ToString()).IsEqualTo(
            "nullable: Retry(0, exponential 250ms ×2, equal jitter, cap 30s) → CircuitBreaker(50% over 30s, min 10, break 15s) → RateLimit(5/1s)");
    }

    [Test]
    public async Task Configuration_Ignores_Scalar_Strategy_Sections()
    {
        var configuration = BuildConfiguration(
            ("Retry", ""),
            ("CircuitBreaker", "invalid"),
            ("RateLimit", "invalid"),
            ("ConcurrencyLimit", "invalid"));
        var services = new ServiceCollection();
        services.AddShield("scalar", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("scalar");

        await Assert.That(shield.ToString()).IsEqualTo("scalar: (empty)");
    }

    [Test]
    public async Task Configuration_Treats_Empty_Failure_Ratio_As_Null()
    {
        var configuration = BuildConfiguration(
            ("CircuitBreaker:ConsecutiveFailures", "2"),
            ("CircuitBreaker:FailureRatio", ""));
        var services = new ServiceCollection();
        services.AddShield("nullable-ratio", configuration);
        using var provider = services.BuildServiceProvider();

        var shield = provider.GetRequiredService<IKevlarRegistry>().GetShield("nullable-ratio");

        await Assert.That(shield.ToString())
            .IsEqualTo("nullable-ratio: CircuitBreaker(2 consecutive, break 15s)");
    }
}

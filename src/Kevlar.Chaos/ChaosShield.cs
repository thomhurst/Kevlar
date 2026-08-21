using Kevlar.Chaos.Internal;

namespace Kevlar.Chaos;

/// <summary>Creates opt-in shields that inject controlled chaos.</summary>
public static class ChaosShield
{
    /// <summary>Creates a shield that injects artificial latency.</summary>
    /// <param name="configure">Configures explicit enablement, blast radius, and delay.</param>
    public static Shield Latency(Action<ChaosLatencyOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }
        var options = new ChaosLatencyOptions();
        configure(options);
        return Shield.Use(new LatencyChaosStrategy(options));
    }

    /// <summary>Creates a shield that short-circuits executions with an exception.</summary>
    /// <param name="configure">Configures explicit enablement, blast radius, and exception.</param>
    public static Shield Fault(Action<ChaosFaultOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }
        var options = new ChaosFaultOptions();
        configure(options);
        return Shield.Use(new FaultChaosStrategy(options));
    }

    /// <summary>Creates a typed shield that short-circuits executions with a result.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="configure">Configures explicit enablement, blast radius, and result.</param>
    public static Shield<TResult> Outcome<TResult>(Action<ChaosOutcomeOptions<TResult>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }
        var options = new ChaosOutcomeOptions<TResult>();
        configure(options);
        return Shield<TResult>.Empty.Use(new OutcomeChaosStrategy<TResult>(options));
    }

    /// <summary>Creates a shield that runs caller-supplied behavior before continuing.</summary>
    /// <param name="configure">Configures explicit enablement, blast radius, and behavior.</param>
    public static Shield Behavior(Action<ChaosBehaviorOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }
        var options = new ChaosBehaviorOptions();
        configure(options);
        return Shield.Use(new BehaviorChaosStrategy(options));
    }
}

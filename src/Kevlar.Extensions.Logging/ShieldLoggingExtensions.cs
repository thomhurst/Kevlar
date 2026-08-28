using Microsoft.Extensions.Logging;
using Kevlar.Strategies;

namespace Kevlar.Extensions.Logging;

#pragma warning disable RS0026 // Typed and untyped overload parity intentionally keeps configure optional.

/// <summary>Adds structured logging to Kevlar shields.</summary>
public static class ShieldLoggingExtensions
{
    /// <summary>Logs strategy events without replacing strategy callbacks.</summary>
    public static Shield WithLogging(
        this Shield shield,
        ILogger logger,
        Action<KevlarLoggingOptions>? configure = null)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        if (logger is null) { throw new ArgumentNullException(nameof(logger)); }

        return WithLogging(shield, logger, CreateOptions(configure));
    }

    internal static Shield WithLogging(
        this Shield shield,
        ILogger logger,
        LoggingOptionsSnapshot options)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        if (logger is null) { throw new ArgumentNullException(nameof(logger)); }
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        shield = shield.CurrentSnapshot;

        return new Shield(
            Prepend(shield.Strategies, new LoggingRegistration(logger, options), shield.Name),
            shield.Ambient,
            shield.Name,
            shield.Time,
            shield.AppliedDecorators);
    }

    /// <summary>Logs strategy events without replacing strategy callbacks.</summary>
    public static Shield<TResult> WithLogging<TResult>(
        this Shield<TResult> shield,
        ILogger logger,
        Action<KevlarLoggingOptions>? configure = null)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        if (logger is null) { throw new ArgumentNullException(nameof(logger)); }

        return WithLogging(shield, logger, CreateOptions(configure));
    }

    internal static Shield<TResult> WithLogging<TResult>(
        this Shield<TResult> shield,
        ILogger logger,
        LoggingOptionsSnapshot options)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        if (logger is null) { throw new ArgumentNullException(nameof(logger)); }
        if (options is null) { throw new ArgumentNullException(nameof(options)); }
        shield = shield.CurrentSnapshot;

        return new Shield<TResult>(
            Prepend(shield.Strategies, new LoggingRegistration(logger, options), shield.Name),
            shield.Ambient,
            shield.Name,
            shield.Time,
            shield.AppliedDecorators);
    }

    private static LoggingOptionsSnapshot CreateOptions(
        Action<KevlarLoggingOptions>? configure)
    {
        var options = new KevlarLoggingOptions();
        configure?.Invoke(options);
        return options.Snapshot();
    }

    private static Strategy[] Prepend(
        Strategy[] strategies,
        LoggingRegistration registration,
        string? shieldName)
    {
        if (strategies.Length > 0 && strategies[0] is LoggingStrategy existing)
        {
            var replacement = new Strategy[strategies.Length];
            Array.Copy(strategies, replacement, strategies.Length);
            var logging = new LoggingStrategy(registration.WithNext(existing.Registration));
            replacement[0] = logging;
            AttachCircuitListeners(
                strategies,
                existing.Listener,
                logging.Listener,
                shieldName);
            return replacement;
        }

        var result = new Strategy[strategies.Length + 1];
        var added = new LoggingStrategy(registration);
        result[0] = added;
        Array.Copy(strategies, 0, result, 1, strategies.Length);
        AttachCircuitListeners(strategies, previous: null, added.Listener, shieldName);
        return result;
    }

    internal static void AttachCircuitListeners(
        Strategy[] strategies,
        IKevlarTelemetryListener? previous,
        IKevlarTelemetryListener listener,
        string? shieldName)
    {
        var strategyIndex = -1;
        foreach (var strategy in strategies)
        {
            if (strategy is LoggingStrategy logging)
            {
                if (previous is not null && ReferenceEquals(logging.Listener, previous))
                {
                    continue;
                }

                break;
            }

            if (strategy is ITransparentStrategy)
            {
                continue;
            }

            strategyIndex++;
            if (strategy is CircuitBreakerStrategy circuitBreaker)
            {
                circuitBreaker.Core.AttachTelemetryListener(
                    previous,
                    listener,
                    shieldName,
                    strategyIndex,
                    scopeOwner: null);
            }
        }
    }
}

#pragma warning restore RS0026

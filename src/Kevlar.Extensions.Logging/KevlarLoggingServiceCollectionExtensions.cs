using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

/// <summary>Registers structured Kevlar logging for dependency-injected shields.</summary>
public static class KevlarLoggingServiceCollectionExtensions
{
    /// <summary>Applies structured logging to shields created by Kevlar DI and HTTP integrations.</summary>
    public static IServiceCollection AddKevlarLogging(
        this IServiceCollection services,
        Action<KevlarLoggingOptions>? configure = null)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }

        var options = new KevlarLoggingOptions();
        configure?.Invoke(options);
        var snapshot = options.Snapshot();
        services.AddLogging();
        services.AddSingleton<IShieldDecorator>(serviceProvider => new LoggingShieldDecorator(
            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Kevlar"),
            snapshot));
        return services;
    }
}

internal sealed class LoggingShieldDecorator(
    ILogger logger,
    LoggingOptionsSnapshot options) : IShieldDecorator
{
    public Shield Decorate(Shield shield, string? name) =>
        EnsureName(shield, name).WithLogging(logger, options);

    public Shield<TResult> Decorate<TResult>(Shield<TResult> shield, string? name) =>
        EnsureName(shield, name).WithLogging(logger, options);

    private static Shield EnsureName(Shield shield, string? name) =>
        shield.Name is null && name is not null ? shield.WithName(name) : shield;

    private static Shield<TResult> EnsureName<TResult>(Shield<TResult> shield, string? name) =>
        shield.Name is null && name is not null ? shield.WithName(name) : shield;
}

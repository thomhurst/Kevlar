using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Registers Kevlar shields with the service collection.</summary>
public static class KevlarServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="IKevlarRegistry"/>. Called automatically by the <c>AddShield</c> overloads.</summary>
    public static IServiceCollection AddKevlar(this IServiceCollection services)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        services.TryAddSingleton<IKevlarRegistry>(sp => new KevlarRegistry(sp, sp.GetServices<ShieldRegistration>()));
        return services;
    }

    /// <summary>Registers a named shield, resolvable via <see cref="IKevlarRegistry.GetShield(string)"/> or as a keyed <see cref="Shield"/> service.</summary>
    public static IServiceCollection AddShield(this IServiceCollection services, string name, Shield shield)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        return services.AddShield(name, _ => shield);
    }

    /// <summary>Registers a named shield built from the service provider.</summary>
    public static IServiceCollection AddShield(this IServiceCollection services, string name, Func<IServiceProvider, Shield> factory)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        services.AddKevlar();
        services.AddSingleton(new ShieldRegistration(name, null, factory));
        services.AddKeyedSingleton(name, (sp, _) => sp.GetRequiredService<IKevlarRegistry>().GetShield(name));
        return services;
    }

    /// <summary>
    /// Registers a named shield bound from <paramref name="configuration"/> (see
    /// <see cref="ShieldDefinition"/> for the schema), so its retry counts, timeouts and breaker
    /// thresholds are tunable without a redeploy. The shield carries <paramref name="name"/> as
    /// its diagnostic name.
    /// </summary>
    public static IServiceCollection AddShield(this IServiceCollection services, string name, IConfiguration configuration)
    {
        if (configuration is null) { throw new ArgumentNullException(nameof(configuration)); }

        return services.AddShield(name, _ =>
        {
            var definition = configuration.Get<ShieldDefinition>() ?? new ShieldDefinition();
            return definition.Build().WithName(name);
        });
    }

    /// <summary>Registers a named result-aware shield, resolvable via <see cref="IKevlarRegistry.GetShield{TResult}(string)"/> or as a keyed <see cref="Shield{TResult}"/> service.</summary>
    public static IServiceCollection AddShield<TResult>(this IServiceCollection services, string name, Shield<TResult> shield)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        return services.AddShield(name, _ => shield);
    }

    /// <summary>Registers a named result-aware shield built from the service provider.</summary>
    public static IServiceCollection AddShield<TResult>(this IServiceCollection services, string name, Func<IServiceProvider, Shield<TResult>> factory)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        services.AddKevlar();
        services.AddSingleton(new ShieldRegistration(name, typeof(TResult), factory));
        services.AddKeyedSingleton(name, (sp, _) => sp.GetRequiredService<IKevlarRegistry>().GetShield<TResult>(name));
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Registers Kevlar policies with the service collection.</summary>
public static class KevlarServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="IKevlarRegistry"/>. Called automatically by the <c>AddKevlarPolicy</c> overloads.</summary>
    public static IServiceCollection AddKevlar(this IServiceCollection services)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        services.TryAddSingleton<IKevlarRegistry>(sp => new KevlarRegistry(sp, sp.GetServices<KevlarPolicyRegistration>()));
        return services;
    }

    /// <summary>Registers a named policy, resolvable via <see cref="IKevlarRegistry.GetPolicy(string)"/> or as a keyed <see cref="Policy"/> service.</summary>
    public static IServiceCollection AddKevlarPolicy(this IServiceCollection services, string name, Policy policy)
    {
        if (policy is null) { throw new ArgumentNullException(nameof(policy)); }
        return services.AddKevlarPolicy(name, _ => policy);
    }

    /// <summary>Registers a named policy built from the service provider.</summary>
    public static IServiceCollection AddKevlarPolicy(this IServiceCollection services, string name, Func<IServiceProvider, Policy> factory)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        services.AddKevlar();
        services.AddSingleton(new KevlarPolicyRegistration(name, null, factory));
        services.AddKeyedSingleton(name, (sp, _) => sp.GetRequiredService<IKevlarRegistry>().GetPolicy(name));
        return services;
    }

    /// <summary>Registers a named result-aware policy, resolvable via <see cref="IKevlarRegistry.GetPolicy{TResult}(string)"/> or as a keyed <see cref="Policy{TResult}"/> service.</summary>
    public static IServiceCollection AddKevlarPolicy<TResult>(this IServiceCollection services, string name, Policy<TResult> policy)
    {
        if (policy is null) { throw new ArgumentNullException(nameof(policy)); }
        return services.AddKevlarPolicy(name, _ => policy);
    }

    /// <summary>Registers a named result-aware policy built from the service provider.</summary>
    public static IServiceCollection AddKevlarPolicy<TResult>(this IServiceCollection services, string name, Func<IServiceProvider, Policy<TResult>> factory)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

        services.AddKevlar();
        services.AddSingleton(new KevlarPolicyRegistration(name, typeof(TResult), factory));
        services.AddKeyedSingleton(name, (sp, _) => sp.GetRequiredService<IKevlarRegistry>().GetPolicy<TResult>(name));
        return services;
    }
}

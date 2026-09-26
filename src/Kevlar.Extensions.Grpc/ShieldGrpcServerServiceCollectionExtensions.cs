using Grpc.Core;
using Kevlar;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Grpc;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers a shared Kevlar interceptor for gRPC server admission control.</summary>
/// <remarks>Also add <see cref="ShieldServerInterceptor"/> to the server's gRPC interceptor collection.</remarks>
public static class ShieldGrpcServerServiceCollectionExtensions
{
    /// <summary>Registers an interceptor that shares the supplied single-invocation shield.</summary>
    public static IServiceCollection AddShieldServerInterceptor(this IServiceCollection services, Shield shield)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        return services.AddSingleton(new ShieldServerInterceptor(shield));
    }

    /// <summary>Registers an interceptor that resolves a named single-invocation shield from the registry.</summary>
    public static IServiceCollection AddShieldServerInterceptor(this IServiceCollection services, string shieldName)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        if (shieldName is null) { throw new ArgumentNullException(nameof(shieldName)); }
        return services.AddSingleton(provider => new ShieldServerInterceptor(
            provider.GetRequiredService<IKevlarRegistry>().GetShield(shieldName)));
    }

    /// <summary>Registers an interceptor with independent state per selected partition, defaulting to method name.</summary>
    public static IServiceCollection AddShieldServerInterceptor(
        this IServiceCollection services,
        PartitionedShield<string> shields,
        Func<ServerCallContext, string>? partitionKey = null)
    {
        if (services is null) { throw new ArgumentNullException(nameof(services)); }
        return services.AddSingleton(new ShieldServerInterceptor(shields, partitionKey));
    }
}

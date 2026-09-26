namespace Kevlar.Extensions.DependencyInjection;

internal sealed class PartitionedShieldRegistration(
    string name,
    Type serviceType,
    Func<IServiceProvider, IReadOnlyList<Strategy>[]> capture)
{
    internal string Name { get; } = name;

    internal Type ServiceType { get; } = serviceType;

    internal IReadOnlyList<Strategy>[] Capture(IServiceProvider serviceProvider) => capture(serviceProvider);
}

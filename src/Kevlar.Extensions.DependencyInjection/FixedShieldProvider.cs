namespace Kevlar.Extensions.DependencyInjection;

internal sealed class FixedShieldProvider(Shield shield) : IShieldProvider
{
    public Shield Current { get; } = shield;
}

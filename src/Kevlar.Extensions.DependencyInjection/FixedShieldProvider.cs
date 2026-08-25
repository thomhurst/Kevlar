namespace Kevlar.Extensions.DependencyInjection;

internal sealed class FixedShieldProvider(Shield shield) : IShieldProvider
{
    public Shield Current { get; } = shield;
}

internal sealed class FixedShieldProvider<TResult>(Shield<TResult> shield) : IShieldProvider<TResult>
{
    public Shield<TResult> Current { get; } = shield;
}

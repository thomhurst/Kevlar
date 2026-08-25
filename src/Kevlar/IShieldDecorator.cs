namespace Kevlar;

/// <summary>Decorates shields created by dependency-injection and HTTP integrations.</summary>
public interface IShieldDecorator
{
    /// <summary>Decorates an untyped shield.</summary>
    Shield Decorate(Shield shield, string? name);

    /// <summary>Decorates a result-aware shield.</summary>
    Shield<TResult> Decorate<TResult>(Shield<TResult> shield, string? name);
}

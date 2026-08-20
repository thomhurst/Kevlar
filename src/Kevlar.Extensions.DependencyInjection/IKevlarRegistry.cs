using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Resolves named shields registered via <c>AddShield</c>.</summary>
public interface IKevlarRegistry
{
    /// <summary>Returns the named shield, or throws <see cref="KeyNotFoundException"/>.</summary>
    Shield GetShield(string name);

    /// <summary>Returns the named result-aware shield, or throws <see cref="KeyNotFoundException"/>.</summary>
    Shield<TResult> GetShield<TResult>(string name);

    /// <summary>Attempts to resolve the named shield.</summary>
    bool TryGetShield(string name, [NotNullWhen(true)] out Shield? shield);

    /// <summary>Attempts to resolve the named result-aware shield.</summary>
    bool TryGetShield<TResult>(string name, [NotNullWhen(true)] out Shield<TResult>? shield);
}

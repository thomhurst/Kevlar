using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Resolves named shields registered via <c>AddShield</c>.</summary>
/// <remarks>
/// Each registration factory runs at most once. Ordinary shields and factory exceptions are
/// cached. Reload-aware registrations cache their provider; <see cref="GetShield(string)"/>
/// returns that provider's current snapshot. Names are case-sensitive; an empty name is valid.
/// </remarks>
public interface IKevlarRegistry
{
    /// <summary>Returns the named shield, or throws <see cref="KeyNotFoundException"/>.</summary>
    Shield GetShield(string name);

    /// <summary>Returns the named void-only shield, or throws <see cref="KeyNotFoundException"/>.</summary>
    VoidShield GetVoidShield(string name);

    /// <summary>Returns the named result-aware shield, or throws <see cref="KeyNotFoundException"/>.</summary>
    Shield<TResult> GetShield<TResult>(string name);

    /// <summary>Attempts to resolve the named shield.</summary>
    bool TryGetShield(string name, [NotNullWhen(true)] out Shield? shield);

    /// <summary>Attempts to resolve the named void-only shield.</summary>
    bool TryGetVoidShield(string name, [NotNullWhen(true)] out VoidShield? shield);

    /// <summary>Attempts to resolve the named result-aware shield.</summary>
    bool TryGetShield<TResult>(string name, [NotNullWhen(true)] out Shield<TResult>? shield);
}

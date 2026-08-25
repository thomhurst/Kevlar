using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Resolves named shields registered via <c>AddShield</c>.</summary>
/// <remarks>
/// Each registration factory runs at most once after a successful build; failed factories may be
/// retried. Reload-aware registrations cache their provider; <see cref="GetShield(string)"/> returns
/// that provider's current snapshot. Names are case-sensitive; an empty name is valid. Removing a
/// registration does not dispose shields already returned to callers.
/// </remarks>
public interface IKevlarRegistry : IDisposable, IAsyncDisposable
{
    /// <summary>Returns the named shield, or throws <see cref="KeyNotFoundException"/>.</summary>
    Shield GetShield(string name);

    /// <summary>Returns the named result-aware shield, or throws <see cref="KeyNotFoundException"/>.</summary>
    Shield<TResult> GetShield<TResult>(string name);

    /// <summary>Attempts to resolve the named shield.</summary>
    bool TryGetShield(string name, [NotNullWhen(true)] out Shield? shield);

    /// <summary>Attempts to resolve the named result-aware shield.</summary>
    bool TryGetShield<TResult>(string name, [NotNullWhen(true)] out Shield<TResult>? shield);

    /// <summary>Returns an existing shield or atomically builds and registers one.</summary>
    Shield GetOrAdd(string name, Func<IServiceProvider, Shield> factory);

    /// <summary>Returns an existing result-aware shield or atomically builds and registers one.</summary>
    Shield<TResult> GetOrAdd<TResult>(string name, Func<IServiceProvider, Shield<TResult>> factory);

    /// <summary>Attempts to add a shield factory without building it.</summary>
    bool TryAdd(string name, Func<IServiceProvider, Shield> factory);

    /// <summary>Attempts to add a result-aware shield factory without building it.</summary>
    bool TryAdd<TResult>(string name, Func<IServiceProvider, Shield<TResult>> factory);

    /// <summary>Removes an untyped registration without disposing shields already held by callers.</summary>
    bool Remove(string name);

    /// <summary>Removes a typed registration without disposing shields already held by callers.</summary>
    bool Remove<TResult>(string name);
}

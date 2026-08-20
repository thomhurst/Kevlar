using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Resolves named policies registered via <c>AddKevlarPolicy</c>.</summary>
public interface IKevlarRegistry
{
    /// <summary>Returns the named policy, or throws <see cref="KeyNotFoundException"/>.</summary>
    Policy GetPolicy(string name);

    /// <summary>Returns the named result-aware policy, or throws <see cref="KeyNotFoundException"/>.</summary>
    Policy<TResult> GetPolicy<TResult>(string name);

    /// <summary>Attempts to resolve the named policy.</summary>
    bool TryGetPolicy(string name, [NotNullWhen(true)] out Policy? policy);

    /// <summary>Attempts to resolve the named result-aware policy.</summary>
    bool TryGetPolicy<TResult>(string name, [NotNullWhen(true)] out Policy<TResult>? policy);
}

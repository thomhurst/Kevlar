using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

internal sealed class ShieldRegistration
{
    public ShieldRegistration(string name, Type? resultType, Func<IServiceProvider, object> factory)
    {
        Name = name;
        ResultType = resultType;
        Factory = factory;
    }

    public string Name { get; }

    /// <summary><see langword="null"/> for non-generic shields; the result type for <see cref="Shield{TResult}"/>.</summary>
    public Type? ResultType { get; }

    public Func<IServiceProvider, object> Factory { get; }
}

internal sealed class KevlarRegistry : IKevlarRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<(string Name, Type? ResultType), ShieldRegistration> _registrations;
    private readonly ConcurrentDictionary<(string Name, Type? ResultType), Lazy<object>> _resolved = new();

    public KevlarRegistry(IServiceProvider serviceProvider, IEnumerable<ShieldRegistration> registrations)
    {
        _serviceProvider = serviceProvider;
        _registrations = [];

        foreach (var registration in registrations)
        {
            // Last registration for a given name wins, matching standard DI override behaviour.
            _registrations[(registration.Name, registration.ResultType)] = registration;
        }
    }

    public Shield GetShield(string name) =>
        TryGetShield(name, out var shield)
            ? shield
            : throw new KeyNotFoundException($"No Kevlar shield named '{name}' has been registered. Register one with AddShield(\"{name}\", ...).");

    public Shield<TResult> GetShield<TResult>(string name) =>
        TryGetShield<TResult>(name, out var shield)
            ? shield
            : throw new KeyNotFoundException($"No Kevlar shield named '{name}' for result type {typeof(TResult).Name} has been registered. Register one with AddShield<{typeof(TResult).Name}>(\"{name}\", ...).");

    public bool TryGetShield(string name, [NotNullWhen(true)] out Shield? shield)
    {
        if (Resolve(name, null) is Shield resolved)
        {
            shield = resolved;
            return true;
        }

        shield = null;
        return false;
    }

    public bool TryGetShield<TResult>(string name, [NotNullWhen(true)] out Shield<TResult>? shield)
    {
        if (Resolve(name, typeof(TResult)) is Shield<TResult> resolved)
        {
            shield = resolved;
            return true;
        }

        shield = null;
        return false;
    }

    private object? Resolve(string name, Type? resultType)
    {
        if (name is null) { throw new ArgumentNullException(nameof(name)); }

        var key = (name, resultType);

        if (_resolved.TryGetValue(key, out var existing))
        {
            return existing.Value;
        }

        if (!_registrations.TryGetValue(key, out var registration))
        {
            return null;
        }

        return _resolved.GetOrAdd(
            key,
            _ => new Lazy<object>(
                () => registration.Factory(_serviceProvider),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Extensions.DependencyInjection;

internal sealed class KevlarPolicyRegistration
{
    public KevlarPolicyRegistration(string name, Type? resultType, Func<IServiceProvider, object> factory)
    {
        Name = name;
        ResultType = resultType;
        Factory = factory;
    }

    public string Name { get; }

    /// <summary><see langword="null"/> for non-generic policies; the result type for <see cref="Policy{TResult}"/>.</summary>
    public Type? ResultType { get; }

    public Func<IServiceProvider, object> Factory { get; }
}

internal sealed class KevlarRegistry : IKevlarRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<(string Name, Type? ResultType), KevlarPolicyRegistration> _registrations;
    private readonly ConcurrentDictionary<(string Name, Type? ResultType), object> _resolved = new();

    public KevlarRegistry(IServiceProvider serviceProvider, IEnumerable<KevlarPolicyRegistration> registrations)
    {
        _serviceProvider = serviceProvider;
        _registrations = [];

        foreach (var registration in registrations)
        {
            // Last registration for a given name wins, matching standard DI override behaviour.
            _registrations[(registration.Name, registration.ResultType)] = registration;
        }
    }

    public Policy GetPolicy(string name) =>
        TryGetPolicy(name, out var policy)
            ? policy
            : throw new KeyNotFoundException($"No Kevlar policy named '{name}' has been registered. Register one with AddKevlarPolicy(\"{name}\", ...).");

    public Policy<TResult> GetPolicy<TResult>(string name) =>
        TryGetPolicy<TResult>(name, out var policy)
            ? policy
            : throw new KeyNotFoundException($"No Kevlar policy named '{name}' for result type {typeof(TResult).Name} has been registered. Register one with AddKevlarPolicy<{typeof(TResult).Name}>(\"{name}\", ...).");

    public bool TryGetPolicy(string name, [NotNullWhen(true)] out Policy? policy)
    {
        if (Resolve(name, null) is Policy resolved)
        {
            policy = resolved;
            return true;
        }

        policy = null;
        return false;
    }

    public bool TryGetPolicy<TResult>(string name, [NotNullWhen(true)] out Policy<TResult>? policy)
    {
        if (Resolve(name, typeof(TResult)) is Policy<TResult> resolved)
        {
            policy = resolved;
            return true;
        }

        policy = null;
        return false;
    }

    private object? Resolve(string name, Type? resultType)
    {
        var key = (name, resultType);

        if (_resolved.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (!_registrations.TryGetValue(key, out var registration))
        {
            return null;
        }

        return _resolved.GetOrAdd(key, _ => registration.Factory(_serviceProvider));
    }
}

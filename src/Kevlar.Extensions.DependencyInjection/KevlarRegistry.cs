using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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

    /// <summary><see langword="null"/> for non-generic shields; otherwise the result type.</summary>
    public Type? ResultType { get; }

    public Func<IServiceProvider, object> Factory { get; }
}

internal sealed class KevlarRegistry : IKevlarRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<(string Name, Type? ResultType), ShieldRegistration> _registrations = new();
    private readonly ConcurrentDictionary<(string Name, Type? ResultType), Lazy<object>> _resolved = new();
    private readonly ReaderWriterLockSlim _lifecycleLock = new();
    private bool _disposed;

    public KevlarRegistry(IServiceProvider serviceProvider, IEnumerable<ShieldRegistration> registrations)
    {
        _serviceProvider = serviceProvider;
        foreach (var registration in registrations)
        {
            _registrations.Add((registration.Name, registration.ResultType), registration);
        }
    }

    public Shield GetShield(string name) => Read(() =>
        TryResolve(name, resultType: null, out Shield? shield)
            ? shield
            : throw MissingShield(name));

    public Shield<TResult> GetShield<TResult>(string name) => Read(() =>
        TryResolve(name, typeof(TResult), out Shield<TResult>? shield)
            ? shield
            : throw MissingShield<TResult>(name));

    public bool TryGetShield(string name, [NotNullWhen(true)] out Shield? shield)
    {
        Shield? found = null;
        var success = Read(() => TryResolve(name, resultType: null, out found));
        shield = found;
        return success;
    }

    public bool TryGetShield<TResult>(string name, [NotNullWhen(true)] out Shield<TResult>? shield)
    {
        Shield<TResult>? found = null;
        var success = Read(() => TryResolve(name, typeof(TResult), out found));
        shield = found;
        return success;
    }

    public Shield GetOrAdd(string name, Func<IServiceProvider, Shield> factory) => Read(() =>
    {
        ThrowIfNull(name, nameof(name));
        ThrowIfNull(factory, nameof(factory));
        var key = (name, (Type?)null);
        _registrations.GetOrAdd(key, _ => new ShieldRegistration(name, null, services => factory(services)));
        return TryResolve(name, resultType: null, out Shield? shield)
            ? shield
            : throw new InvalidOperationException($"The factory for shield '{name}' returned an incompatible value.");
    });

    public Shield<TResult> GetOrAdd<TResult>(
        string name,
        Func<IServiceProvider, Shield<TResult>> factory) => Read(() =>
    {
        ThrowIfNull(name, nameof(name));
        ThrowIfNull(factory, nameof(factory));
        var resultType = typeof(TResult);
        var key = (name, (Type?)resultType);
        _registrations.GetOrAdd(
            key,
            _ => new ShieldRegistration(name, resultType, services => factory(services)));
        return TryResolve(name, resultType, out Shield<TResult>? shield)
            ? shield
            : throw new InvalidOperationException(
                $"The factory for shield '{name}' and result type {resultType.Name} returned an incompatible value.");
    });

    public bool TryAdd(string name, Func<IServiceProvider, Shield> factory) => Read(() =>
    {
        ThrowIfNull(name, nameof(name));
        ThrowIfNull(factory, nameof(factory));
        return _registrations.TryAdd(
            (name, null),
            new ShieldRegistration(name, null, services => factory(services)));
    });

    public bool TryAdd<TResult>(string name, Func<IServiceProvider, Shield<TResult>> factory) => Read(() =>
    {
        ThrowIfNull(name, nameof(name));
        ThrowIfNull(factory, nameof(factory));
        var resultType = typeof(TResult);
        return _registrations.TryAdd(
            (name, resultType),
            new ShieldRegistration(name, resultType, services => factory(services)));
    });

    public bool Remove(string name) => Remove(name, resultType: null);

    public bool Remove<TResult>(string name) => Remove(name, typeof(TResult));

    public void Dispose()
    {
        var values = BeginDispose();
        if (values is null)
        {
            return;
        }

        var failures = new List<Exception>();
        foreach (var provider in GetReloadingProviders(values))
        {
            TryDispose(provider, failures);
        }

        foreach (var strategy in GetStrategies(values))
        {
            TryDispose(strategy, failures);
        }

        ThrowDisposalFailures(failures);
    }

    public async ValueTask DisposeAsync()
    {
        var values = BeginDispose();
        if (values is null)
        {
            return;
        }

        var failures = new List<Exception>();
        foreach (var provider in GetReloadingProviders(values))
        {
            TryDispose(provider, failures);
        }

        foreach (var strategy in GetStrategies(values))
        {
            try
            {
                if (strategy is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (strategy is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        ThrowDisposalFailures(failures);
    }

    private bool Remove(string name, Type? resultType) => Read(() =>
    {
        ThrowIfNull(name, nameof(name));
        var key = (name, resultType);
        var removed = _registrations.TryRemove(key, out _);
        _resolved.TryRemove(key, out _);
        return removed;
    });

    private bool TryResolve<TResult>(
        string name,
        Type resultType,
        [NotNullWhen(true)] out Shield<TResult>? shield)
    {
        ThrowIfNull(name, nameof(name));
        var value = Resolve(name, resultType);
        if (value is Shield<TResult> direct)
        {
            shield = direct;
            return true;
        }

        if (value is IShieldProvider<TResult> provider)
        {
            shield = provider.Current;
            return true;
        }

        shield = null;
        return false;
    }

    private bool TryResolve(string name, Type? resultType, [NotNullWhen(true)] out Shield? shield)
    {
        ThrowIfNull(name, nameof(name));
        var value = Resolve(name, resultType);
        if (value is Shield direct)
        {
            shield = direct;
            return true;
        }

        if (value is IShieldProvider provider)
        {
            shield = provider.Current;
            return true;
        }

        shield = null;
        return false;
    }

    private object? Resolve(string name, Type? resultType)
    {
        var key = (name, resultType);
        if (!_registrations.TryGetValue(key, out var registration))
        {
            return null;
        }

        var lazy = _resolved.GetOrAdd(
            key,
            _ => new Lazy<object>(
                () => registration.Factory(_serviceProvider)
                    ?? throw new InvalidOperationException($"The factory for shield '{name}' returned null."),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return lazy.Value;
        }
        catch
        {
            _ = ((ICollection<KeyValuePair<(string Name, Type? ResultType), Lazy<object>>>)_resolved)
                .Remove(new(key, lazy));
            throw;
        }
    }

    private TResult Read<TResult>(Func<TResult> action)
    {
        _lifecycleLock.EnterReadLock();
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(IKevlarRegistry));
            }

            return action();
        }
        finally
        {
            _lifecycleLock.ExitReadLock();
        }
    }

    private List<object>? BeginDispose()
    {
        _lifecycleLock.EnterWriteLock();
        try
        {
            if (_disposed)
            {
                return null;
            }

            _disposed = true;
            var values = new List<object>();
            foreach (var lazy in _resolved.Values)
            {
                if (lazy.IsValueCreated)
                {
                    values.Add(lazy.Value);
                }
            }

            return values;
        }
        finally
        {
            _lifecycleLock.ExitWriteLock();
        }
    }

    private static IEnumerable<IDisposable> GetReloadingProviders(IEnumerable<object> values)
    {
        var seen = new HashSet<object>(ReferenceComparer<object>.Instance);
        foreach (var value in values)
        {
            if (value is IReloadingProvider provider && seen.Add(provider))
            {
                yield return provider;
            }
        }
    }

    private static IEnumerable<Strategy> GetStrategies(IEnumerable<object> values)
    {
        var seen = new HashSet<Strategy>(ReferenceComparer<Strategy>.Instance);
        foreach (var value in values)
        {
            if (value is IReloadingProvider provider)
            {
                foreach (var snapshot in provider.Snapshots)
                {
                    foreach (var strategy in GetStrategies(snapshot, seen))
                    {
                        yield return strategy;
                    }
                }
            }
            else if (value is IShieldLifecycle shield)
            {
                foreach (var strategy in GetStrategies(shield, seen))
                {
                    yield return strategy;
                }
            }
        }
    }

    private static IEnumerable<Strategy> GetStrategies(
        IShieldLifecycle shield,
        HashSet<Strategy> seen)
    {
        for (var index = shield.Strategies.Length - 1; index >= 0; index--)
        {
            var strategy = shield.Strategies[index];
            if (seen.Add(strategy))
            {
                yield return strategy;
            }
        }
    }

    private static void TryDispose(object value, List<Exception> failures)
    {
        try
        {
            if (value is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (value is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void ThrowDisposalFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private static KeyNotFoundException MissingShield(string name) => new(
        $"No Kevlar shield named '{name}' has been registered. Register one with AddShield(\"{name}\", ...).");

    private static KeyNotFoundException MissingShield<TResult>(string name) => new(
        $"No Kevlar shield named '{name}' for result type {typeof(TResult).Name} has been registered. " +
        $"Register one with AddShield<{typeof(TResult).Name}>(\"{name}\", ...).");

    private static void ThrowIfNull(object? value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static ReferenceComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

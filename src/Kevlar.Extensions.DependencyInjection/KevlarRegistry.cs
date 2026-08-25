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

internal sealed class RegistryEntry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ShieldRegistration _registration;
    private Lazy<object> _value;

    public RegistryEntry(IServiceProvider serviceProvider, ShieldRegistration registration)
    {
        _serviceProvider = serviceProvider;
        _registration = registration;
        _value = CreateValue();
    }

    public object Resolve()
    {
        var value = Volatile.Read(ref _value);
        try
        {
            return value.Value;
        }
        catch
        {
            Interlocked.CompareExchange(ref _value, CreateValue(), value);
            throw;
        }
    }

    public bool TryGetResolved([NotNullWhen(true)] out object? resolved)
    {
        var value = Volatile.Read(ref _value);
        if (!value.IsValueCreated)
        {
            resolved = null;
            return false;
        }

        resolved = value.Value;
        return true;
    }

    private Lazy<object> CreateValue() => new(
        () => _registration.Factory(_serviceProvider)
            ?? throw new InvalidOperationException(
                $"The factory for shield '{_registration.Name}' returned null."),
        LazyThreadSafetyMode.ExecutionAndPublication);
}

internal sealed class KevlarRegistry : IKevlarRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<(string Name, Type? ResultType), RegistryEntry> _entries = new();
    private readonly ConcurrentQueue<RegistryEntry> _retiredEntries = new();
    private readonly ConcurrentDictionary<IReloadingProvider, byte> _reloadingProviders =
        new(ReferenceComparer<IReloadingProvider>.Instance);
    private readonly object _lifecycleLock = new();
    private int _activeOperations;
    private bool _disposed;

    public KevlarRegistry(IServiceProvider serviceProvider, IEnumerable<ShieldRegistration> registrations)
    {
        _serviceProvider = serviceProvider;
        foreach (var registration in registrations)
        {
            // Last registration for a given name wins, matching standard DI override behaviour.
            _entries[(registration.Name, registration.ResultType)] =
                new RegistryEntry(_serviceProvider, registration);
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
        var entry = _entries.GetOrAdd(
            key,
            _ => new RegistryEntry(
                _serviceProvider,
                new ShieldRegistration(name, null, services => factory(services))));
        return TryResolve(entry, out Shield? shield)
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
        var entry = _entries.GetOrAdd(
            key,
            _ => new RegistryEntry(
                _serviceProvider,
                new ShieldRegistration(name, resultType, services => factory(services))));
        return TryResolve(entry, out Shield<TResult>? shield)
            ? shield
            : throw new InvalidOperationException(
                $"The factory for shield '{name}' and result type {resultType.Name} returned an incompatible value.");
    });

    public bool TryAdd(string name, Func<IServiceProvider, Shield> factory) => Read(() =>
    {
        ThrowIfNull(name, nameof(name));
        ThrowIfNull(factory, nameof(factory));
        return _entries.TryAdd(
            (name, null),
            new RegistryEntry(
                _serviceProvider,
                new ShieldRegistration(name, null, services => factory(services))));
    });

    public bool TryAdd<TResult>(string name, Func<IServiceProvider, Shield<TResult>> factory) => Read(() =>
    {
        ThrowIfNull(name, nameof(name));
        ThrowIfNull(factory, nameof(factory));
        var resultType = typeof(TResult);
        return _entries.TryAdd(
            (name, resultType),
            new RegistryEntry(
                _serviceProvider,
                new ShieldRegistration(name, resultType, services => factory(services))));
    });

    public bool Remove(string name) => Remove(name, resultType: null);

    public bool Remove<TResult>(string name) => Remove(name, typeof(TResult));

    internal TProvider CreateReloadingProvider<TProvider>(Func<TProvider> factory)
        where TProvider : class, IReloadingProvider => Read(() =>
    {
        var provider = factory();
        _reloadingProviders.TryAdd(provider, 0);
        return provider;
    });

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
        if (!_entries.TryRemove(key, out var entry))
        {
            return false;
        }

        _retiredEntries.Enqueue(entry);
        return true;
    });

    private bool TryResolve<TResult>(
        string name,
        Type resultType,
        [NotNullWhen(true)] out Shield<TResult>? shield)
    {
        ThrowIfNull(name, nameof(name));
        if (_entries.TryGetValue((name, resultType), out var entry))
        {
            return TryResolve(entry, out shield);
        }

        shield = null;
        return false;
    }

    private static bool TryResolve<TResult>(
        RegistryEntry entry,
        [NotNullWhen(true)] out Shield<TResult>? shield)
    {
        var value = entry.Resolve();
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
        if (_entries.TryGetValue((name, resultType), out var entry))
        {
            return TryResolve(entry, out shield);
        }

        shield = null;
        return false;
    }

    private static bool TryResolve(
        RegistryEntry entry,
        [NotNullWhen(true)] out Shield? shield)
    {
        var value = entry.Resolve();
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

    private TResult Read<TResult>(Func<TResult> action)
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(IKevlarRegistry));
            }

            _activeOperations++;
        }

        try
        {
            return action();
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _activeOperations--;
                if (_activeOperations == 0)
                {
                    Monitor.PulseAll(_lifecycleLock);
                }
            }
        }
    }

    private List<object>? BeginDispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return null;
            }

            _disposed = true;
            while (_activeOperations > 0)
            {
                Monitor.Wait(_lifecycleLock);
            }

            var values = new List<object>(_reloadingProviders.Keys);
            foreach (var entry in _entries.Values)
            {
                if (entry.TryGetResolved(out var value))
                {
                    values.Add(value);
                }
            }

            foreach (var entry in _retiredEntries)
            {
                if (entry.TryGetResolved(out var value))
                {
                    values.Add(value);
                }
            }

            return values;
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

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
    private readonly object _retirementLock = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ShieldRegistration _registration;
    private readonly Action<object> _validatePublication;
    private readonly Action<object> _retireRejectedValue;
    private Lazy<object> _value;
    private object? _resolved;
    private Action<object>? _retirementHandler;
    private bool _retirementPublished;

    public RegistryEntry(
        IServiceProvider serviceProvider,
        ShieldRegistration registration,
        Action<object> validatePublication,
        Action<object> retireRejectedValue)
    {
        _serviceProvider = serviceProvider;
        _registration = registration;
        _validatePublication = validatePublication;
        _retireRejectedValue = retireRejectedValue;
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
        resolved = Volatile.Read(ref _resolved);
        return resolved is not null;
    }

    public void MarkRemoved(Action<object> retirementHandler)
    {
        object? resolved = null;
        lock (_retirementLock)
        {
            _retirementHandler = retirementHandler;
            if (_resolved is not null && !_retirementPublished)
            {
                _retirementPublished = true;
                resolved = _resolved;
            }
        }

        if (resolved is not null)
        {
            retirementHandler(resolved);
        }
    }

    private Lazy<object> CreateValue() => new(
        () =>
        {
            var value = _registration.Factory(_serviceProvider)
                ?? throw new InvalidOperationException(
                    $"The factory for shield '{_registration.Name}' returned null.");
            if (value is IShieldLifecycle shield)
            {
                ShieldRetirement.Track(shield);
            }

            try
            {
                _validatePublication(value);
            }
            catch
            {
                _retireRejectedValue(value);
                throw;
            }

            Action<object>? retirementHandler = null;
            lock (_retirementLock)
            {
                Volatile.Write(ref _resolved, value);
                if (_retirementHandler is not null && !_retirementPublished)
                {
                    _retirementPublished = true;
                    retirementHandler = _retirementHandler;
                }
            }

            retirementHandler?.Invoke(value);

            return value;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);
}

internal sealed class KevlarRegistry : IKevlarRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<(string Name, Type? ResultType), RegistryEntry> _entries = new();
    private readonly ConcurrentDictionary<ShieldRetirement, byte> _retirements = new();
    private readonly ConcurrentQueue<Exception> _retirementFailures = new();
    private readonly StrategyDisposalTracker _strategyDisposals = new();
    private readonly ConcurrentDictionary<IReloadingProvider, byte> _reloadingProviders =
        new(ReferenceComparer<IReloadingProvider>.Instance);
    private readonly object _lifecycleLock = new();
    private HashSet<Strategy>? _reclamationRetainedStrategies;
    private int _activeOperations;
    private int _pendingDeferredDisposals;
    private int _reclamationThreadId;
    private bool _reclaiming;
    private bool _disposed;

    public KevlarRegistry(IServiceProvider serviceProvider, IEnumerable<ShieldRegistration> registrations)
    {
        _serviceProvider = serviceProvider;
        foreach (var registration in registrations)
        {
            // Last registration for a given name wins, matching standard DI override behaviour.
            _entries[(registration.Name, registration.ResultType)] =
                new RegistryEntry(_serviceProvider, registration, ValidatePublication, Retire);
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
                new ShieldRegistration(name, null, services => factory(services)),
                ValidatePublication,
                Retire));
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
                new ShieldRegistration(name, resultType, services => factory(services)),
                ValidatePublication,
                Retire));
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
                new ShieldRegistration(name, null, services => factory(services)),
                ValidatePublication,
                Retire));
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
                new ShieldRegistration(name, resultType, services => factory(services)),
                ValidatePublication,
                Retire));
    });

    public bool Remove(string name) => Remove(name, resultType: null);

    public bool Remove<TResult>(string name) => Remove(name, typeof(TResult));

    internal TProvider CreateReloadingProvider<TProvider>(Func<TProvider> factory)
        where TProvider : class, IReloadingProvider => Read(() =>
    {
        var provider = factory();
        try
        {
            ValidatePublication(provider);
            _reloadingProviders.TryAdd(provider, 0);
            provider.SetLifecycleHandlers(
                ReclaimRetirements,
                ProtectPublication,
                ValidatePublication);
            return provider;
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    });

    public void Dispose()
    {
        var values = BeginDispose();
        if (values is null)
        {
            return;
        }

        var failures = new List<Exception>();
        DrainRetirementFailures(failures);
        foreach (var provider in GetReloadingProviders(values))
        {
            TryDispose(provider, failures);
        }

        foreach (var strategy in GetStrategies(values))
        {
            if (_strategyDisposals.TryClaim(strategy))
            {
                TryDispose(strategy, failures);
            }
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
        DrainRetirementFailures(failures);
        foreach (var provider in GetReloadingProviders(values))
        {
            TryDispose(provider, failures);
        }

        foreach (var strategy in GetStrategies(values))
        {
            if (!_strategyDisposals.TryClaim(strategy))
            {
                continue;
            }

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

        entry.MarkRemoved(Retire);

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
        if (!TryBeginOperation())
        {
            throw new ObjectDisposedException(nameof(IKevlarRegistry));
        }

        try
        {
            return action();
        }
        finally
        {
            EndOperation();
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
            while (_activeOperations > 0 || _reclaiming || _pendingDeferredDisposals > 0)
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

            foreach (var retirement in _retirements.Keys)
            {
                values.Add(retirement.Strategies);
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
                foreach (var strategy in provider.Strategies)
                {
                    if (seen.Add(strategy))
                    {
                        yield return strategy;
                    }
                }
            }
            else if (value is Strategy[] strategies)
            {
                for (var index = strategies.Length - 1; index >= 0; index--)
                {
                    if (seen.Add(strategies[index]))
                    {
                        yield return strategies[index];
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

    private void Retire(object value)
    {
        if (value is IReloadingProvider provider)
        {
            var (retirements, cleanupFailure) = provider.Retire();
            foreach (var retirement in retirements)
            {
                _retirements.TryAdd(retirement, 0);
            }

            _reloadingProviders.TryRemove(provider, out _);
            if (cleanupFailure is not null)
            {
                _retirementFailures.Enqueue(cleanupFailure);
            }

            return;
        }

        if (value is IShieldLifecycle shield)
        {
            _retirements.TryAdd(new ShieldRetirement(value, shield), 0);
        }
    }

    private void ScavengeRetirements()
    {
        if (!TryBeginReclamation())
        {
            return;
        }

        List<IAsyncDisposable>? deferredAsyncDisposals = null;
        try
        {
            List<ShieldRetirement>? reclaimable = null;
            foreach (var retirement in _retirements.Keys)
            {
                if (retirement.CanReclaim() && _retirements.TryRemove(retirement, out _))
                {
                    reclaimable ??= [];
                    reclaimable.Add(retirement);
                }
            }

            if (reclaimable is null)
            {
                return;
            }

            deferredAsyncDisposals = [];
            ReclaimRetirementsCore(reclaimable, deferredAsyncDisposals);
            if (deferredAsyncDisposals.Count > 0)
            {
                BeginDeferredDisposals();
            }
        }
        finally
        {
            EndReclamation();
        }

        if (deferredAsyncDisposals.Count > 0)
        {
            CompleteDeferredDisposals(deferredAsyncDisposals);
        }
    }

    private void ReclaimRetirements(IReadOnlyList<ShieldRetirement> reclaimable)
    {
        foreach (var retirement in reclaimable)
        {
            _retirements.TryAdd(retirement, 0);
        }

        ScavengeRetirements();
    }

    private void ReclaimRetirementsCore(
        IReadOnlyList<ShieldRetirement> reclaimable,
        List<IAsyncDisposable> deferredAsyncDisposals)
    {
        var retainedOrClaimed = ShieldRetirement.CreateStrategySet();
        AddPublishedStrategies(retainedOrClaimed);

        lock (_lifecycleLock)
        {
            _reclamationRetainedStrategies = retainedOrClaimed;
        }

        try
        {
            foreach (var retirement in reclaimable)
            {
                retirement.Reclaim(
                    _retirementFailures.Enqueue,
                    retainedOrClaimed,
                    _strategyDisposals,
                    deferredAsyncDisposals);
            }
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _reclamationRetainedStrategies = null;
            }
        }
    }

    private void CompleteDeferredDisposals(List<IAsyncDisposable> deferredAsyncDisposals)
    {
        try
        {
            foreach (var disposable in deferredAsyncDisposals)
            {
                try
                {
                    disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    _retirementFailures.Enqueue(exception);
                }
            }
        }
        finally
        {
            EndDeferredDisposals();
        }
    }

    private void BeginDeferredDisposals()
    {
        lock (_lifecycleLock)
        {
            _pendingDeferredDisposals++;
        }
    }

    private void EndDeferredDisposals()
    {
        lock (_lifecycleLock)
        {
            _pendingDeferredDisposals--;
            Monitor.PulseAll(_lifecycleLock);
        }
    }

    private void AddPublishedStrategies(HashSet<Strategy> retainedOrClaimed)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.TryGetResolved(out var resolved))
            {
                AddStrategies(resolved, retainedOrClaimed);
            }
        }

        foreach (var provider in _reloadingProviders.Keys)
        {
            retainedOrClaimed.UnionWith(provider.Strategies);
        }

        foreach (var retirement in _retirements.Keys)
        {
            retainedOrClaimed.UnionWith(retirement.Strategies);
        }
    }

    private void ValidatePublication(object value)
    {
        var strategies = value switch
        {
            IReloadingProvider provider => provider.Strategies,
            IShieldLifecycle shield => shield.Strategies,
            _ => [],
        };

        foreach (var strategy in strategies)
        {
            if (_strategyDisposals.IsClaimed(strategy))
            {
                throw new InvalidOperationException(
                    "A shield cannot publish a strategy whose registry-owned disposal has started.");
            }
        }
    }

    private void ProtectPublication(Action publish)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            publish();
        }
        finally
        {
            EndOperation();
        }
    }

    private bool TryBeginOperation()
    {
        lock (_lifecycleLock)
        {
            var currentThreadId = Environment.CurrentManagedThreadId;
            while (_reclaiming && _reclamationThreadId != currentThreadId)
            {
                Monitor.Wait(_lifecycleLock);
            }

            if (_disposed)
            {
                return false;
            }

            _activeOperations++;
            return true;
        }
    }

    private void EndOperation()
    {
        var scavenge = false;
        HashSet<Strategy>? retainedOrClaimed = null;
        lock (_lifecycleLock)
        {
            if (_reclaiming
                && _reclamationThreadId == Environment.CurrentManagedThreadId
                && _reclamationRetainedStrategies is { } reclamationStrategies)
            {
                retainedOrClaimed = reclamationStrategies;
            }

            _activeOperations--;
            if (_activeOperations == 0)
            {
                scavenge = !_disposed;
                Monitor.PulseAll(_lifecycleLock);
            }
        }

        if (retainedOrClaimed is not null)
        {
            AddPublishedStrategies(retainedOrClaimed);
        }

        if (scavenge)
        {
            ScavengeRetirements();
        }
    }

    private bool TryBeginReclamation()
    {
        lock (_lifecycleLock)
        {
            if (_disposed || _reclaiming || _activeOperations != 0)
            {
                return false;
            }

            _reclaiming = true;
            _reclamationThreadId = Environment.CurrentManagedThreadId;
            return true;
        }
    }

    private void EndReclamation()
    {
        lock (_lifecycleLock)
        {
            _reclamationThreadId = 0;
            _reclaiming = false;
            Monitor.PulseAll(_lifecycleLock);
        }
    }

    private static void AddStrategies(object value, HashSet<Strategy> strategies)
    {
        if (value is IReloadingProvider provider)
        {
            strategies.UnionWith(provider.Strategies);
        }
        else if (value is IShieldLifecycle shield)
        {
            strategies.UnionWith(shield.Strategies);
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

    private void DrainRetirementFailures(List<Exception> failures)
    {
        while (_retirementFailures.TryDequeue(out var failure))
        {
            failures.Add(failure);
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

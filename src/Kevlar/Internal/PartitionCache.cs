namespace Kevlar.Internal;

internal sealed class PartitionCache<TKey, TShield> : IDisposable, IAsyncDisposable
    where TKey : notnull
    where TShield : class, IShieldLifecycle
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _mutationGate = new(initialCount: 1, maxCount: 1);
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly Dictionary<TKey, Creation> _creations;
    private readonly Func<TKey, ValueTask<TShield>> _factory;
    private readonly int _maximumPartitions;
    private readonly TimeSpan? _idleExpiration;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TKey, TShield, ValueTask>? _onCreated;
    private readonly Func<TKey, TShield, PartitionEvictionReason, ValueTask>? _onEvicted;
    private readonly AsyncLocal<EvictionCallbackScope?> _evictionCallback = new();
    private readonly Queue<Exception> _disposalFailures = new();
    private readonly List<Task> _pendingDisposals = [];

    private Entry? _leastRecentlyUsed;
    private Entry? _mostRecentlyUsed;
    private TaskCompletionSource<bool> _capacityChanged = CreateCapacitySignal();
    private int _reservedSlots;
    private long _createdCount;
    private long _capacityEvictionCount;
    private long _expirationEvictionCount;
    private long _clearedEvictionCount;
    private TaskCompletionSource<bool>? _factoryCompletion;
    private TaskCompletionSource<bool>? _disposeCompletion;
    private int _activeFactories;
    private int _disposed;

    public PartitionCache(
        Func<TKey, ValueTask<TShield>> factory,
        PartitionCacheOptions<TKey, TShield> options,
        IEqualityComparer<TKey>? comparer)
    {
        Throw.IfNull(factory, nameof(factory));
        Throw.IfOutOfRange(
            options.MaxPartitions <= 0,
            nameof(options),
            "MaxPartitions must be positive.");
        Throw.IfOutOfRange(
            options.IdleExpiration is { } idleExpiration && idleExpiration <= TimeSpan.Zero,
            nameof(options),
            "IdleExpiration must be positive when specified.");
        Throw.IfNull(options.TimeProvider, nameof(options));

        _factory = factory;
        _maximumPartitions = options.MaxPartitions;
        _idleExpiration = options.IdleExpiration;
        _timeProvider = options.TimeProvider;
        _onCreated = options.OnCreated;
        _onEvicted = options.OnEvicted;
        _entries = new Dictionary<TKey, Entry>(comparer);
        _creations = new Dictionary<TKey, Creation>(comparer);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long CreatedCount => ReadCounter(ref _createdCount);

    public long CapacityEvictionCount => ReadCounter(ref _capacityEvictionCount);

    public long ExpirationEvictionCount => ReadCounter(ref _expirationEvictionCount);

    public long ClearedEvictionCount => ReadCounter(ref _clearedEvictionCount);

    public long EvictionCount
    {
        get
        {
            lock (_gate)
            {
                return _capacityEvictionCount + _expirationEvictionCount + _clearedEvictionCount;
            }
        }
    }

    public PartitionCacheState CaptureState()
    {
        lock (_gate)
        {
            return new PartitionCacheState(
                _entries.Count,
                _createdCount,
                _capacityEvictionCount,
                _expirationEvictionCount,
                _clearedEvictionCount);
        }
    }

    public TShield Get(TKey key)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        if (TryGetWarm(key, out var shield, out _))
        {
            return shield;
        }

        return GetSlowAsync(key).AsTask().GetAwaiter().GetResult();
    }

    public ValueTask<TShield> GetAsync(TKey key)
    {
        ThrowIfDisposed();
        ValidateKey(key);

        if (TryGetWarm(key, out var shield, out _))
        {
            return new ValueTask<TShield>(shield);
        }

        return GetSlowAsync(key);
    }

    public bool TryGet(TKey key, out TShield? shield)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        if (TryGetWarm(key, out shield, out var pruneDue))
        {
            return true;
        }

        if (!pruneDue)
        {
            return false;
        }

        shield = TryGetSlowAsync(key).AsTask().GetAwaiter().GetResult();
        return shield is not null;
    }

    public bool TryRemove(TKey key) =>
        TryRemoveAsync(key).AsTask().GetAwaiter().GetResult();

    public ValueTask<bool> TryRemoveAsync(TKey key)
    {
        ThrowIfDisposed();
        ValidateKey(key);
        return TryRemoveSlowAsync(key);
    }

    public void Clear() => ClearAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask ClearAsync()
    {
        ThrowIfDisposed();
        return ClearSlowAsync();
    }

    public int PruneExpired() =>
        PruneExpiredAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask<int> PruneExpiredAsync()
    {
        ThrowIfDisposed();
        return _idleExpiration is null
            ? new ValueTask<int>(0)
            : PruneExpiredSlowAsync();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        var created = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Interlocked.CompareExchange(ref _disposeCompletion, created, null);
        if (completion is not null)
        {
            return new ValueTask(completion.Task);
        }

        Volatile.Write(ref _disposed, 1);
        _ = CompleteDisposalAsync(created);
        return new ValueTask(created.Task);
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            await ClearSlowAsync().ConfigureAwait(false);
            await GetFactoryCompletionTask().ConfigureAwait(false);
            Task[] pendingDisposals;
            lock (_gate)
            {
                pendingDisposals = _pendingDisposals.ToArray();
            }

            await Task.WhenAll(pendingDisposals).ConfigureAwait(false);
            Exception[] failures;
            lock (_gate)
            {
                failures = _disposalFailures.ToArray();
                _disposalFailures.Clear();
            }

            if (failures.Length == 1)
            {
                throw failures[0];
            }

            if (failures.Length > 1)
            {
                throw new AggregateException(failures);
            }

            completion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async ValueTask<TShield> GetSlowAsync(TKey key)
    {
        Creation creation;
        var creates = false;
        var createsUnretained = false;
        TShield? retained = null;
        List<Entry>? expired;

        await _mutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                expired = PruneExpiredUnderLock();
                ReserveSlots(expired);
                if (_entries.TryGetValue(key, out var existing))
                {
                    Touch(existing, Timestamp());
                    retained = existing.Shield;
                    creation = null!;
                }
                else if (_creations.TryGetValue(key, out creation!))
                {
                    var callback = _evictionCallback.Value;
                    createsUnretained = callback is { Active: true }
                        && (callback.Blocks(creation)
                            || (callback.OwnsReservation
                                && _entries.Count + _reservedSlots >= _maximumPartitions));
                }
                else
                {
                    creation = new Creation();
                    _creations.Add(key, creation);
                    creates = true;
                }

                if (creates || createsUnretained)
                {
                    BeginFactoryUnderLock();
                }
            }

        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyAutomaticEvictionsAsync(
                expired,
                PartitionEvictionReason.Expiration,
                creates ? creation : null)
            .ConfigureAwait(false);

        if (retained is not null)
        {
            return retained;
        }

        if (!creates && !createsUnretained)
        {
            return await creation.Task.ConfigureAwait(false);
        }

        try
        {
            TShield shield;
            Entry entry;
            try
            {
                shield = await _factory(key).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The partition factory returned null.");
                entry = await CreateEntryAsync(key, shield).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (creates)
                {
                    FailCreation(key, creation, exception);
                }

                throw;
            }

            if (createsUnretained)
            {
                TrackUnretained(entry);
                return shield;
            }

            try
            {
                await PublishAsync(key, entry, creation).ConfigureAwait(false);
                return shield;
            }
            catch (Exception exception)
            {
                FailCreation(key, creation, exception);
                await DisposeEvictedAsync(entry).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            CompleteFactory();
        }
    }

    private async ValueTask PublishAsync(TKey key, Entry entry, Creation creation)
    {
        var ownedReservations = 0;
        try
        {
            while (true)
            {
                List<Entry>? expired = null;
                Entry? capacityEviction = null;
                Task? waitForCapacity = null;
                var published = false;
                var completedUnretained = false;

                await _mutationGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    lock (_gate)
                    {
                        if (ownedReservations > 0)
                        {
                            Publish(key, entry);
                            _reservedSlots -= ownedReservations;
                            ownedReservations = 0;
                            PulseCapacityChanged();
                            published = true;
                        }
                        else
                        {
                            if (_evictionCallback.Value is
                                { Active: true, OwnsReservation: true }
                                && _entries.Count + _reservedSlots >= _maximumPartitions)
                            {
                                // This callback owns capacity that cannot be reused before it
                                // returns. Complete its nested lookup without retaining it so the
                                // outer publisher keeps the reservation and capacity invariant.
                                _creations.Remove(key);
                                completedUnretained = true;
                            }
                            else
                            {
                                expired = PruneExpiredUnderLock();
                                if (expired is not null)
                                {
                                    ownedReservations = expired.Count;
                                    _reservedSlots += ownedReservations;
                                }
                                else if (_entries.Count + _reservedSlots < _maximumPartitions)
                                {
                                    Publish(key, entry);
                                    published = true;
                                }
                                else if (_leastRecentlyUsed is { } eviction)
                                {
                                    capacityEviction = eviction;
                                    RemoveEntry(capacityEviction);
                                    _capacityEvictionCount++;
                                    _reservedSlots++;
                                    ownedReservations = 1;
                                }
                                else
                                {
                                    waitForCapacity = _capacityChanged.Task;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    _mutationGate.Release();
                }

                if (completedUnretained)
                {
                    TrackUnretained(entry);
                    creation.Succeed(entry.Shield);
                    return;
                }

                if (waitForCapacity is not null)
                {
                    await waitForCapacity.ConfigureAwait(false);
                    continue;
                }

                await NotifyEvictedAsync(
                        expired,
                        PartitionEvictionReason.Expiration,
                        blockedCreation: creation)
                    .ConfigureAwait(false);
                if (capacityEviction is not null)
                {
                    await NotifyEvictedAsync(
                            capacityEviction,
                            PartitionEvictionReason.Capacity,
                            ownsReservation: true,
                            blockedCreation: creation)
                        .ConfigureAwait(false);
                    await DisposeEvictedAsync(capacityEviction).ConfigureAwait(false);
                }

                if (published)
                {
                    await NotifyCreatedAsync(key, entry.Shield).ConfigureAwait(false);
                    creation.Succeed(entry.Shield);
                    return;
                }
            }
        }
        finally
        {
            ReleaseReservations(ownedReservations);
        }
    }

    private async ValueTask<TShield?> TryGetSlowAsync(TKey key)
    {
        TShield? shield = null;
        List<Entry>? expired;
        await _mutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                expired = PruneExpiredUnderLock();
                ReserveSlots(expired);
                if (_entries.TryGetValue(key, out var entry))
                {
                    Touch(entry, Timestamp());
                    shield = entry.Shield;
                }
            }
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyAutomaticEvictionsAsync(expired, PartitionEvictionReason.Expiration)
            .ConfigureAwait(false);
        return shield;
    }

    private async ValueTask<bool> TryRemoveSlowAsync(TKey key)
    {
        Entry? removed = null;
        List<Entry>? expired;
        await _mutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                expired = PruneExpiredUnderLock();
                ReserveSlots(expired);
                if (_entries.TryGetValue(key, out removed))
                {
                    RemoveEntry(removed);
                    _clearedEvictionCount++;
                }
            }
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyAutomaticEvictionsAsync(expired, PartitionEvictionReason.Expiration)
            .ConfigureAwait(false);
        if (removed is not null)
        {
            await NotifyEvictedAsync(removed, PartitionEvictionReason.Cleared).ConfigureAwait(false);
            await DisposeEvictedAsync(removed).ConfigureAwait(false);
        }

        return removed is not null;
    }

    private async ValueTask ClearSlowAsync()
    {
        Entry[] removed;
        await _mutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                removed = _entries.Values.ToArray();
                _entries.Clear();
                _leastRecentlyUsed = null;
                _mostRecentlyUsed = null;
                _clearedEvictionCount += removed.Length;
            }
        }
        finally
        {
            _mutationGate.Release();
        }

        foreach (var entry in removed)
        {
            await NotifyEvictedAsync(entry, PartitionEvictionReason.Cleared).ConfigureAwait(false);
            await DisposeEvictedAsync(entry).ConfigureAwait(false);
        }
    }

    private async ValueTask<int> PruneExpiredSlowAsync()
    {
        List<Entry>? expired;
        await _mutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                expired = PruneExpiredUnderLock();
                ReserveSlots(expired);
            }
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyAutomaticEvictionsAsync(expired, PartitionEvictionReason.Expiration)
            .ConfigureAwait(false);
        return expired?.Count ?? 0;
    }

    private List<Entry>? PruneExpiredUnderLock()
    {
        if (_idleExpiration is not { } idleExpiration)
        {
            return null;
        }

        List<Entry>? expired = null;
        var now = Timestamp();
        while (_leastRecentlyUsed is { } entry
            && _timeProvider.GetElapsedTime(entry.LastAccess, now) >= idleExpiration)
        {
            RemoveEntry(entry);
            _expirationEvictionCount++;
            (expired ??= []).Add(entry);
        }

        return expired;
    }

    private async ValueTask NotifyCreatedAsync(TKey key, TShield shield)
    {
        if (_onCreated is null)
        {
            return;
        }

        try
        {
            await _onCreated(key, shield).ConfigureAwait(false);
        }
        catch
        {
            // Lifecycle observers must not change partition behavior.
        }
    }

    private async ValueTask NotifyEvictedAsync(
        List<Entry>? entries,
        PartitionEvictionReason reason,
        bool ownsReservation = false,
        Creation? blockedCreation = null)
    {
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            await NotifyEvictedAsync(entry, reason, ownsReservation, blockedCreation)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask NotifyEvictedAsync(
        Entry entry,
        PartitionEvictionReason reason,
        bool ownsReservation = false,
        Creation? blockedCreation = null)
    {
        var previousScope = _evictionCallback.Value;
        var scope = new EvictionCallbackScope(
            ownsReservation,
            blockedCreation,
            previousScope);
        _evictionCallback.Value = scope;
        try
        {
            try
            {
                KevlarMetrics.PartitionEviction(reason);
            }
            catch
            {
                // Metric listeners must not change partition behavior.
            }

            if (_onEvicted is null)
            {
                return;
            }

            try
            {
                await _onEvicted(entry.Key, entry.Shield, reason)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Lifecycle observers must not change partition behavior.
            }
        }
        finally
        {
            var unretained = scope.TakeUnretained();
            scope.Deactivate();
            _evictionCallback.Value = previousScope;
            await DisposeEvictedAsync(unretained).ConfigureAwait(false);
        }
    }

    private async ValueTask NotifyAutomaticEvictionsAsync(
        List<Entry>? entries,
        PartitionEvictionReason reason,
        Creation? blockedCreation = null)
    {
        try
        {
            await NotifyEvictedAsync(
                    entries,
                    reason,
                    ownsReservation: true,
                    blockedCreation: blockedCreation)
                .ConfigureAwait(false);
            await DisposeEvictedAsync(entries).ConfigureAwait(false);
        }
        finally
        {
            ReleaseReservations(entries?.Count ?? 0);
        }
    }

    private void Publish(TKey key, Entry entry)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(typeof(PartitionCache<TKey, TShield>).Name);
        }

        entry.LastAccess = Timestamp();
        _entries.Add(key, entry);
        AddMostRecentlyUsed(entry);
        _createdCount++;
        _creations.Remove(key);
    }

    private void ReserveSlots(List<Entry>? entries)
    {
        if (entries is not null)
        {
            _reservedSlots += entries.Count;
        }
    }

    private void ReleaseReservations(int count)
    {
        if (count == 0)
        {
            return;
        }

        lock (_gate)
        {
            _reservedSlots -= count;
            PulseCapacityChanged();
        }
    }

    private void PulseCapacityChanged()
    {
        var previous = _capacityChanged;
        _capacityChanged = CreateCapacitySignal();
        previous.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CreateCapacitySignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void FailCreation(TKey key, Creation creation, Exception exception)
    {
        lock (_gate)
        {
            if (_creations.TryGetValue(key, out var current)
                && ReferenceEquals(current, creation))
            {
                _creations.Remove(key);
            }
        }

        creation.Fail(exception);
    }

    private long Timestamp() => _idleExpiration is null ? 0 : _timeProvider.GetTimestamp();

    private bool TryGetWarm(
        TKey key,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TShield? shield,
        out bool pruneDue)
    {
        lock (_gate)
        {
            var now = Timestamp();
            if (_idleExpiration is { } idleExpiration
                && _leastRecentlyUsed is { } leastRecentlyUsed
                && _timeProvider.GetElapsedTime(leastRecentlyUsed.LastAccess, now) >= idleExpiration)
            {
                shield = null;
                pruneDue = true;
                return false;
            }

            pruneDue = false;
            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing, now);
                shield = existing.Shield;
                return true;
            }
        }

        shield = null;
        return false;
    }

    private void Touch(Entry entry, long now)
    {
        entry.LastAccess = now;
        if (ReferenceEquals(entry, _mostRecentlyUsed))
        {
            return;
        }

        Unlink(entry);
        AddMostRecentlyUsed(entry);
    }

    private void AddMostRecentlyUsed(Entry entry)
    {
        entry.Previous = _mostRecentlyUsed;
        entry.Next = null;
        if (_mostRecentlyUsed is null)
        {
            _leastRecentlyUsed = entry;
        }
        else
        {
            _mostRecentlyUsed.Next = entry;
        }

        _mostRecentlyUsed = entry;
    }

    private void RemoveEntry(Entry entry)
    {
        _entries.Remove(entry.Key);
        Unlink(entry);
    }

    private ValueTask DisposeEvictedAsync(Entry entry) =>
        DisposeEvictedAsync([entry]);

    private async ValueTask<Entry> CreateEntryAsync(TKey key, TShield shield)
    {
        var executionTracker = shield.Strategies.Length == 0
            ? null
            : shield.Strategies[0].EnableExecutionTracking();
        List<Strategy>? owned = null;
        Exception? acquisitionFailure = null;
        foreach (var strategy in shield.Strategies)
        {
            if ((strategy is IDisposable || strategy is IAsyncDisposable)
                && !ContainsReference(owned, strategy))
            {
                if (StrategyOwnership.TryAcquire(strategy))
                {
                    (owned ??= []).Add(strategy);
                }
                else
                {
                    acquisitionFailure ??= new InvalidOperationException(
                        "A partition cannot publish a strategy whose disposal has started.");
                }
            }
        }

        if (acquisitionFailure is not null)
        {
            await ReleaseOwnedStrategiesAsync(owned ?? []).ConfigureAwait(false);
            throw acquisitionFailure;
        }

        return new Entry(
            key,
            shield,
            lastAccess: 0,
            executionTracker,
            owned?.ToArray() ?? []);
    }

    private static bool ContainsReference(List<Strategy>? strategies, Strategy candidate)
    {
        if (strategies is not null)
        {
            foreach (var strategy in strategies)
            {
                if (ReferenceEquals(strategy, candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async ValueTask DisposeEvictedAsync(IReadOnlyList<Entry>? entries)
    {
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (!entry.TryBeginDisposal())
            {
                continue;
            }

            if (entry.ExecutionTracker is null || entry.ExecutionTracker.ActiveExecutions == 0)
            {
                await ReleaseOwnedStrategiesAsync(entry.OwnedStrategies).ConfigureAwait(false);
                continue;
            }

            TrackPendingDisposal(DisposeWhenInactiveAsync(entry));
        }
    }

    private async Task DisposeWhenInactiveAsync(Entry entry)
    {
        while (entry.ExecutionTracker!.ActiveExecutions != 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        }

        await ReleaseOwnedStrategiesAsync(entry.OwnedStrategies).ConfigureAwait(false);
    }

    private void TrackPendingDisposal(Task pending)
    {
        lock (_gate)
        {
            _pendingDisposals.RemoveAll(static task => task.IsCompleted);
            _pendingDisposals.Add(pending);
        }
    }

    private async ValueTask ReleaseOwnedStrategiesAsync(IReadOnlyList<Strategy> strategies)
    {
        for (var index = strategies.Count - 1; index >= 0; index--)
        {
            var strategy = strategies[index];
            if (StrategyOwnership.Release(strategy))
            {
                await DisposeStrategyAsync(strategy).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask DisposeStrategyAsync(Strategy strategy)
    {
        try
        {
            if (strategy is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                ((IDisposable)strategy).Dispose();
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _disposalFailures.Enqueue(exception);
            }
        }
    }

    private void Unlink(Entry entry)
    {
        if (entry.Previous is null)
        {
            _leastRecentlyUsed = entry.Next;
        }
        else
        {
            entry.Previous.Next = entry.Next;
        }

        if (entry.Next is null)
        {
            _mostRecentlyUsed = entry.Previous;
        }
        else
        {
            entry.Next.Previous = entry.Previous;
        }

        entry.Previous = null;
        entry.Next = null;
    }

    private static void ValidateKey(TKey key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }
    }

    private long ReadCounter(ref long counter)
    {
        lock (_gate)
        {
            return counter;
        }
    }

    private sealed class Entry(
        TKey key,
        TShield shield,
        long lastAccess,
        StrategyExecutionTracker? executionTracker,
        Strategy[] ownedStrategies)
    {
        private int _disposalStarted;

        public TKey Key { get; } = key;

        public TShield Shield { get; } = shield;

        public StrategyExecutionTracker? ExecutionTracker { get; } = executionTracker;

        public Strategy[] OwnedStrategies { get; } = ownedStrategies;

        public long LastAccess { get; set; } = lastAccess;

        public Entry? Previous { get; set; }

        public Entry? Next { get; set; }

        public bool TryBeginDisposal() =>
            Interlocked.Exchange(ref _disposalStarted, 1) == 0;
    }

    private sealed class Creation
    {
        private readonly TaskCompletionSource<TShield> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TShield> Task => _completion.Task;

        public void Succeed(TShield shield) => _completion.TrySetResult(shield);

        public void Fail(Exception exception)
        {
            _completion.TrySetException(exception);
            _ = _completion.Task.Exception;
        }
    }

    private sealed class EvictionCallbackScope(
        bool ownsReservation,
        Creation? blockedCreation,
        EvictionCallbackScope? parent)
    {
        private int _active = 1;
        private List<Entry>? _unretained;

        public bool Active => Volatile.Read(ref _active) != 0;

        public bool OwnsReservation => ownsReservation
            || parent is { Active: true, OwnsReservation: true };

        public bool Blocks(Creation creation) =>
            ReferenceEquals(blockedCreation, creation)
            || parent is { Active: true } && parent.Blocks(creation);

        public void AddUnretained(Entry entry)
        {
            lock (this)
            {
                (_unretained ??= []).Add(entry);
            }
        }

        public Entry[]? TakeUnretained()
        {
            lock (this)
            {
                var entries = _unretained?.ToArray();
                _unretained = null;
                return entries;
            }
        }

        public void Deactivate() => Volatile.Write(ref _active, 0);
    }

    private void TrackUnretained(Entry entry)
    {
        if (_evictionCallback.Value is { Active: true } scope)
        {
            scope.AddUnretained(entry);
            return;
        }

        TrackPendingDisposal(DisposeEvictedAsync(entry).AsTask());
    }

    private void BeginFactoryUnderLock()
    {
        _activeFactories++;
    }

    private void CompleteFactory()
    {
        TaskCompletionSource<bool>? completion = null;
        lock (_gate)
        {
            if (--_activeFactories == 0)
            {
                completion = _factoryCompletion;
                _factoryCompletion = null;
            }
        }

        completion?.TrySetResult(true);
    }

    private Task GetFactoryCompletionTask()
    {
        lock (_gate)
        {
            return _activeFactories == 0
                ? Task.CompletedTask
                : (_factoryCompletion ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(typeof(PartitionCache<TKey, TShield>).Name);
        }
    }

}

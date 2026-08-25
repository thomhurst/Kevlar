namespace Kevlar.Internal;

internal sealed class PartitionCache<TKey, TShield>
    where TKey : notnull
    where TShield : class
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _mutationGate = new(initialCount: 1, maxCount: 1);
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly Dictionary<TKey, Creation> _creations;
    private readonly Func<TKey, ValueTask<TShield>> _factory;
    private readonly int _maximumPartitions;
    private readonly TimeSpan? _idleExpiration;
    private readonly TimeProvider _timeProvider;
    private readonly Action<PartitionCreatedNotification>? _onCreated;
    private readonly Func<PartitionCreatedNotification, ValueTask>? _onCreatedAsync;
    private readonly Action<PartitionEvictionNotification>? _onEvicted;
    private readonly Func<PartitionEvictionNotification, ValueTask>? _onEvictedAsync;
    private readonly AsyncLocal<EvictionCallbackScope?> _evictionCallback = new();

    private Entry? _leastRecentlyUsed;
    private Entry? _mostRecentlyUsed;
    private TaskCompletionSource<bool> _capacityChanged = CreateCapacitySignal();
    private int _reservedSlots;
    private long _createdCount;
    private long _capacityEvictionCount;
    private long _expirationEvictionCount;
    private long _clearedEvictionCount;

    public PartitionCache(
        Func<TKey, ValueTask<TShield>> factory,
        PartitionedShieldOptions? options,
        IEqualityComparer<TKey>? comparer)
    {
        Throw.IfNull(factory, nameof(factory));
        options ??= new PartitionedShieldOptions();
        Throw.IfOutOfRange(
            options.MaximumPartitions <= 0,
            nameof(options),
            "MaximumPartitions must be positive.");
        Throw.IfOutOfRange(
            options.IdleExpiration is { } idleExpiration && idleExpiration <= TimeSpan.Zero,
            nameof(options),
            "IdleExpiration must be positive when specified.");
        Throw.IfNull(options.TimeProvider, nameof(options));

        _factory = factory;
        _maximumPartitions = options.MaximumPartitions;
        _idleExpiration = options.IdleExpiration;
        _timeProvider = options.TimeProvider;
        _onCreated = options.OnCreated;
        _onCreatedAsync = options.OnCreatedAsync;
        _onEvicted = options.OnEvicted;
        _onEvictedAsync = options.OnEvictedAsync;
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
        ValidateKey(key);
        if (TryGetWarm(key, out var shield))
        {
            return shield;
        }

        return GetSlowAsync(key).AsTask().GetAwaiter().GetResult();
    }

    public ValueTask<TShield> GetAsync(TKey key)
    {
        ValidateKey(key);

        if (TryGetWarm(key, out var shield))
        {
            return new ValueTask<TShield>(shield);
        }

        return GetSlowAsync(key);
    }

    public bool TryGet(TKey key, out TShield? shield)
    {
        ValidateKey(key);
        if (_idleExpiration is null)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var entry))
                {
                    Touch(entry, now: 0);
                    shield = entry.Shield;
                    return true;
                }

                shield = null;
                return false;
            }
        }

        shield = TryGetSlowAsync(key).AsTask().GetAwaiter().GetResult();
        return shield is not null;
    }

    public bool TryRemove(TKey key) =>
        TryRemoveAsync(key).AsTask().GetAwaiter().GetResult();

    public ValueTask<bool> TryRemoveAsync(TKey key)
    {
        ValidateKey(key);
        return TryRemoveSlowAsync(key);
    }

    public void Clear() => ClearAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask ClearAsync() => ClearSlowAsync();

    public int PruneExpired() =>
        PruneExpiredAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask<int> PruneExpiredAsync() =>
        _idleExpiration is null
            ? new ValueTask<int>(0)
            : PruneExpiredSlowAsync();

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
            }

        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyAutomaticEvictionsAsync(
                expired,
                PartitionEvictionReason.Idle,
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

        TShield shield;
        try
        {
            shield = await _factory(key).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The partition factory returned null.");
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
            return shield;
        }

        try
        {
            await PublishAsync(key, shield, creation).ConfigureAwait(false);
            return shield;
        }
        catch (Exception exception)
        {
            FailCreation(key, creation, exception);
            throw;
        }
    }

    private async ValueTask PublishAsync(TKey key, TShield shield, Creation creation)
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
                            Publish(key, shield, creation);
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
                                    Publish(key, shield, creation);
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
                    creation.Succeed(shield);
                    return;
                }

                if (waitForCapacity is not null)
                {
                    await waitForCapacity.ConfigureAwait(false);
                    continue;
                }

                await NotifyEvictedAsync(
                        expired,
                        PartitionEvictionReason.Idle,
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
                }

                if (published)
                {
                    await NotifyCreatedAsync(key, shield).ConfigureAwait(false);
                    creation.Succeed(shield);
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

        await NotifyAutomaticEvictionsAsync(expired, PartitionEvictionReason.Idle)
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

        await NotifyAutomaticEvictionsAsync(expired, PartitionEvictionReason.Idle)
            .ConfigureAwait(false);
        if (removed is not null)
        {
            await NotifyEvictedAsync(removed, PartitionEvictionReason.Cleared).ConfigureAwait(false);
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

        await NotifyAutomaticEvictionsAsync(expired, PartitionEvictionReason.Idle)
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
        if (_onCreated is null && _onCreatedAsync is null)
        {
            return;
        }

        var notification = new PartitionCreatedNotification(key, shield);
        try
        {
            _onCreated?.Invoke(notification);
        }
        catch
        {
            // Lifecycle observers must not change partition behavior.
        }

        if (_onCreatedAsync is not null)
        {
            try
            {
                await _onCreatedAsync(notification).ConfigureAwait(false);
            }
            catch
            {
                // Lifecycle observers must not change partition behavior.
            }
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

            if (_onEvicted is null && _onEvictedAsync is null)
            {
                return;
            }

            var notification = new PartitionEvictionNotification(entry.Key, entry.Shield, reason);
            try
            {
                _onEvicted?.Invoke(notification);
            }
            catch
            {
                // Lifecycle observers must not change partition behavior.
            }

            if (_onEvictedAsync is not null)
            {
                try
                {
                    await _onEvictedAsync(notification).ConfigureAwait(false);
                }
                catch
                {
                    // Lifecycle observers must not change partition behavior.
                }
            }
        }
        finally
        {
            scope.Deactivate();
            _evictionCallback.Value = previousScope;
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
        }
        finally
        {
            ReleaseReservations(entries?.Count ?? 0);
        }
    }

    private void Publish(TKey key, TShield shield, Creation creation)
    {
        var entry = new Entry(key, shield, Timestamp());
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

    private bool TryGetWarm(TKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TShield? shield)
    {
        if (_idleExpiration is null)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var existing))
                {
                    Touch(existing, now: 0);
                    shield = existing.Shield;
                    return true;
                }
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

    private sealed class Entry(TKey key, TShield shield, long lastAccess)
    {
        public TKey Key { get; } = key;

        public TShield Shield { get; } = shield;

        public long LastAccess { get; set; } = lastAccess;

        public Entry? Previous { get; set; }

        public Entry? Next { get; set; }
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

        public bool Active => Volatile.Read(ref _active) != 0;

        public bool OwnsReservation => ownsReservation
            || parent is { Active: true, OwnsReservation: true };

        public bool Blocks(Creation creation) =>
            ReferenceEquals(blockedCreation, creation)
            || parent is { Active: true } && parent.Blocks(creation);

        public void Deactivate() => Volatile.Write(ref _active, 0);
    }

}

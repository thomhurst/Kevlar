using System.Runtime.ExceptionServices;

namespace Kevlar.Internal;

internal sealed class PartitionCache<TKey, TShield>
    where TKey : notnull
    where TShield : class
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly Dictionary<TKey, Creation> _creations;
    private readonly Func<TKey, TShield> _factory;
    private readonly int _maximumPartitions;
    private readonly TimeSpan? _idleExpiration;
    private readonly TimeProvider _timeProvider;

    private Entry? _leastRecentlyUsed;
    private Entry? _mostRecentlyUsed;
    private long _createdCount;
    private long _capacityEvictionCount;
    private long _expirationEvictionCount;

    public PartitionCache(
        Func<TKey, TShield> factory,
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

    public long CreatedCount
    {
        get
        {
            lock (_gate)
            {
                return _createdCount;
            }
        }
    }

    public long CapacityEvictionCount
    {
        get
        {
            lock (_gate)
            {
                return _capacityEvictionCount;
            }
        }
    }

    public long ExpirationEvictionCount
    {
        get
        {
            lock (_gate)
            {
                return _expirationEvictionCount;
            }
        }
    }

    public TShield Get(TKey key)
    {
        ValidateKey(key);
        Creation creation;
        var creates = false;
        lock (_gate)
        {
            var now = PruneExpiredUnderLock();
            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing, now);
                return existing.Shield;
            }

            if (!_creations.TryGetValue(key, out creation!))
            {
                creation = new Creation();
                _creations.Add(key, creation);
                creates = true;
            }
        }

        if (!creates)
        {
            var completedShield = creation.Wait();
            lock (_gate)
            {
                var now = PruneExpiredUnderLock();
                if (_entries.TryGetValue(key, out var completedEntry)
                    && ReferenceEquals(completedEntry.Shield, completedShield))
                {
                    Touch(completedEntry, now);
                }
            }

            return completedShield;
        }

        TShield shield;
        try
        {
            shield = _factory(key)
                ?? throw new InvalidOperationException("The partition factory returned null.");
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                creation.Fail(exception);
                if (_creations.TryGetValue(key, out var current)
                    && ReferenceEquals(current, creation))
                {
                    _creations.Remove(key);
                }
            }
            throw;
        }

        try
        {
            lock (_gate)
            {
                var now = PruneExpiredUnderLock();
                if (_entries.TryGetValue(key, out var existing))
                {
                    Touch(existing, now);
                    shield = existing.Shield;
                }
                else
                {
                    if (_entries.Count == _maximumPartitions)
                    {
                        RemoveEntry(_leastRecentlyUsed!);
                        _capacityEvictionCount++;
                    }

                    var entry = new Entry(key, shield, now);
                    _entries.Add(key, entry);
                    AddMostRecentlyUsed(entry);
                    _createdCount++;
                }

                creation.Succeed(shield);
                _creations.Remove(key);
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                creation.Fail(exception);
                if (_creations.TryGetValue(key, out var current)
                    && ReferenceEquals(current, creation))
                {
                    _creations.Remove(key);
                }
            }
            throw;
        }
        return shield;
    }

    public bool TryGet(TKey key, out TShield? shield)
    {
        ValidateKey(key);
        lock (_gate)
        {
            var now = PruneExpiredUnderLock();
            if (_entries.TryGetValue(key, out var entry))
            {
                Touch(entry, now);
                shield = entry.Shield;
                return true;
            }

            shield = null;
            return false;
        }
    }

    public bool Remove(TKey key)
    {
        ValidateKey(key);
        lock (_gate)
        {
            _ = PruneExpiredUnderLock();
            if (!_entries.TryGetValue(key, out var entry))
            {
                return false;
            }

            RemoveEntry(entry);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _leastRecentlyUsed = null;
            _mostRecentlyUsed = null;
        }
    }

    public int PruneExpired()
    {
        if (_idleExpiration is null)
        {
            return 0;
        }

        lock (_gate)
        {
            var previousCount = _entries.Count;
            _ = PruneExpiredUnderLock();
            return previousCount - _entries.Count;
        }
    }

    private long PruneExpiredUnderLock()
    {
        if (_idleExpiration is not { } idleExpiration)
        {
            return 0;
        }

        var now = _timeProvider.GetTimestamp();
        while (_leastRecentlyUsed is { } entry
            && _timeProvider.GetElapsedTime(entry.LastAccess, now) >= idleExpiration)
        {
            RemoveEntry(entry);
            _expirationEvictionCount++;
        }

        return now;
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
        private TShield? _shield;
        private ExceptionDispatchInfo? _failure;
        private bool _completed;

        public TShield Wait()
        {
            lock (this)
            {
                while (!_completed)
                {
                    Monitor.Wait(this);
                }

                _failure?.Throw();
                return _shield!;
            }
        }

        public void Succeed(TShield shield)
        {
            lock (this)
            {
                _shield = shield;
                _completed = true;
                Monitor.PulseAll(this);
            }
        }

        public void Fail(Exception exception)
        {
            lock (this)
            {
                _failure = ExceptionDispatchInfo.Capture(exception);
                _completed = true;
                Monitor.PulseAll(this);
            }
        }
    }
}

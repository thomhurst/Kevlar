using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// A strongly typed property bag carried by <see cref="KevlarContext"/> for passing
/// custom data between the caller, execution delegate, and strategy callbacks. The bag is
/// pooled with its context and must not be retained beyond the current callback or delegate.
/// </summary>
public sealed class KevlarProperties
{
    private const int RetainedSlotCapacity = 32;

    private PropertyIdentity _firstIdentity;
    private PropertySlot? _firstItem;
    private Dictionary<PropertyIdentity, PropertySlot>? _items;
    private int _count;

#if DEBUG
    private bool _returnedToPool;
#endif

    internal KevlarProperties()
    {
    }

    /// <summary>Gets the number of values currently stored in the bag.</summary>
    public int Count
    {
        get
        {
            ThrowIfReturnedToPool();
            return _count;
        }
    }

    /// <summary>Stores a value under the given key, replacing any existing value.</summary>
    public void Set<T>(KevlarKey<T> key, T value)
    {
        ThrowIfReturnedToPool();
        var identity = GetIdentity(key);
        Set(identity, value);
    }

    /// <summary>Attempts to read the value stored under the given key.</summary>
    public bool TryGet<T>(KevlarKey<T> key, out T value)
    {
        ThrowIfReturnedToPool();
        var identity = GetIdentity(key);
        if (_firstItem is not null &&
            _firstIdentity == identity &&
            _firstItem is PropertySlot<T> firstSlot &&
            firstSlot.TryGet(out value))
        {
            return true;
        }

        if (_items is not null &&
            _items.TryGetValue(identity, out var stored) &&
            stored is PropertySlot<T> slot &&
            slot.TryGet(out value))
        {
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Reads the value stored under the given key, or returns <paramref name="defaultValue"/>.</summary>
    public T GetOrDefault<T>(KevlarKey<T> key, T defaultValue = default!) =>
        TryGet(key, out var value) ? value : defaultValue;

    /// <summary>Returns whether a value is stored under the given key.</summary>
    public bool Contains<T>(KevlarKey<T> key)
    {
        ThrowIfReturnedToPool();
        var identity = GetIdentity(key);
        return Find(identity) is { HasValue: true };
    }

    /// <summary>Removes the value stored under the given key.</summary>
    /// <returns><see langword="true"/> when a value was removed; otherwise <see langword="false"/>.</returns>
    public bool Remove<T>(KevlarKey<T> key)
    {
        ThrowIfReturnedToPool();
        var identity = GetIdentity(key);
        var slot = Find(identity);
        if (slot is not { HasValue: true })
        {
            return false;
        }

        slot.Clear();
        _count--;
        return true;
    }

    internal void Clear()
    {
        if (_firstItem is null)
        {
            return;
        }

        _count = 0;

        if (_items is { Count: >= RetainedSlotCapacity })
        {
            _firstIdentity = default;
            _firstItem = null;
            _items = null;
            return;
        }

        _firstItem.Clear();
        if (_items is not null)
        {
            foreach (var slot in _items.Values)
            {
                slot.Clear();
            }
        }
    }

    internal void CopyTo(KevlarProperties target)
    {
        if (_firstItem is null)
        {
            return;
        }

        _firstItem.CopyTo(target, _firstIdentity);
        if (_items is not null)
        {
            foreach (var pair in _items)
            {
                pair.Value.CopyTo(target, pair.Key);
            }
        }
    }

    private void Set<T>(PropertyIdentity identity, T value)
    {
        if (_firstItem is null)
        {
            _firstIdentity = identity;
            _firstItem = new PropertySlot<T>(value);
            _count = 1;
            return;
        }

        if (_firstIdentity == identity)
        {
            if (((PropertySlot<T>)_firstItem).Set(value))
            {
                _count++;
            }

            return;
        }

        var items = _items ??= [];
        if (items.TryGetValue(identity, out var stored))
        {
            if (((PropertySlot<T>)stored).Set(value))
            {
                _count++;
            }
        }
        else
        {
            items.Add(identity, new PropertySlot<T>(value));
            _count++;
        }
    }

    private PropertySlot? Find(PropertyIdentity identity)
    {
        if (_firstItem is not null && _firstIdentity == identity)
        {
            return _firstItem;
        }

        return _items is not null && _items.TryGetValue(identity, out var stored)
            ? stored
            : null;
    }

    private static PropertyIdentity GetIdentity<T>(KevlarKey<T> key)
    {
        if (key.Name is null)
        {
            throw new InvalidOperationException("KevlarKey<T> must be created with a name");
        }

        return new(key.Name, typeof(T));
    }

    internal void MarkRented()
    {
#if DEBUG
        _returnedToPool = false;
#endif
    }

    internal void MarkReturned()
    {
#if DEBUG
        _returnedToPool = true;
#endif
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void ThrowIfReturnedToPool()
    {
#if DEBUG
        if (_returnedToPool)
        {
            throw new InvalidOperationException(
                "This KevlarProperties instance belongs to a KevlarContext that has been returned " +
                "to Kevlar's context pool and is no longer valid.");
        }
#endif
    }

    private readonly record struct PropertyIdentity(string Name, Type ValueType);

    private abstract class PropertySlot
    {
        public abstract bool HasValue { get; }

        public abstract void Clear();

        public abstract void CopyTo(KevlarProperties target, PropertyIdentity identity);
    }

    private sealed class PropertySlot<T>(T value) : PropertySlot
    {
        private T _value = value;
        private bool _hasValue = true;

        public override bool HasValue => _hasValue;

        public bool Set(T value)
        {
            var wasEmpty = !_hasValue;
            _value = value;
            _hasValue = true;
            return wasEmpty;
        }

        public bool TryGet(out T value)
        {
            value = _value;
            return _hasValue;
        }

        public override void Clear()
        {
            _value = default!;
            _hasValue = false;
        }

        public override void CopyTo(KevlarProperties target, PropertyIdentity identity)
        {
            if (_hasValue)
            {
                target.Set(identity, _value);
            }
        }
    }
}

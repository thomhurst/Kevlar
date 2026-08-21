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

    private Dictionary<PropertyIdentity, PropertySlot>? _items;

    internal KevlarProperties()
    {
    }

    /// <summary>Stores a value under the given key, replacing any existing value.</summary>
    public void Set<T>(KevlarKey<T> key, T value)
    {
        var identity = GetIdentity(key);
        Set(identity, value);
    }

    /// <summary>Attempts to read the value stored under the given key.</summary>
    public bool TryGet<T>(KevlarKey<T> key, out T value)
    {
        var identity = GetIdentity(key);
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

    internal void Clear()
    {
        if (_items is null)
        {
            return;
        }

        if (_items.Count > RetainedSlotCapacity)
        {
            _items = null;
            return;
        }

        foreach (var slot in _items.Values)
        {
            slot.Clear();
        }
    }

    internal void CopyTo(KevlarProperties target)
    {
        if (_items is null || _items.Count == 0)
        {
            return;
        }

        foreach (var pair in _items)
        {
            pair.Value.CopyTo(target, pair.Key);
        }
    }

    private void Set<T>(PropertyIdentity identity, T value)
    {
        var items = _items ??= [];
        if (items.TryGetValue(identity, out var stored))
        {
            ((PropertySlot<T>)stored).Set(value);
            return;
        }

        items.Add(identity, new PropertySlot<T>(value));
    }

    private static PropertyIdentity GetIdentity<T>(KevlarKey<T> key)
    {
        Throw.IfNull(key.Name, nameof(key));
        return new(key.Name, typeof(T));
    }

    private readonly record struct PropertyIdentity(string Name, Type ValueType);

    private abstract class PropertySlot
    {
        public abstract void Clear();

        public abstract void CopyTo(KevlarProperties target, PropertyIdentity identity);
    }

    private sealed class PropertySlot<T>(T value) : PropertySlot
    {
        private T _value = value;
        private bool _hasValue = true;

        public void Set(T value)
        {
            _value = value;
            _hasValue = true;
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

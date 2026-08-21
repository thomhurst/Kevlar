namespace Kevlar;

/// <summary>
/// A strongly typed property bag carried by <see cref="KevlarContext"/> for passing
/// custom data between the caller and strategy callbacks.
/// </summary>
public sealed class KevlarProperties
{
    private Dictionary<PropertyIdentity, object?>? _items;

    internal KevlarProperties()
    {
    }

    /// <summary>Stores a value under the given key, replacing any existing value.</summary>
    public void Set<T>(KevlarKey<T> key, T value) => (_items ??= [])[new(key.Name, typeof(T))] = value;

    /// <summary>Attempts to read the value stored under the given key.</summary>
    public bool TryGet<T>(KevlarKey<T> key, out T value)
    {
        if (_items is not null && _items.TryGetValue(new(key.Name, typeof(T)), out var stored))
        {
            value = (T)stored!;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Reads the value stored under the given key, or returns <paramref name="defaultValue"/>.</summary>
    public T GetOrDefault<T>(KevlarKey<T> key, T defaultValue = default!) =>
        TryGet(key, out var value) ? value : defaultValue;

    internal void Clear() => _items?.Clear();

    internal void CopyTo(KevlarProperties target)
    {
        if (_items is null || _items.Count == 0)
        {
            return;
        }

        var items = target._items ??= [];
        foreach (var pair in _items)
        {
            items[pair.Key] = pair.Value;
        }
    }

    private readonly record struct PropertyIdentity(string Name, Type ValueType);
}

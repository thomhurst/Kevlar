using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// A strongly typed key for storing values in <see cref="KevlarProperties"/>.
/// </summary>
/// <typeparam name="T">The type of the value stored under this key.</typeparam>
public readonly struct KevlarKey<T>
{
    /// <summary>Creates a key with the given name. Names are case-sensitive.</summary>
    public KevlarKey(string name)
    {
        Throw.IfNull(name, nameof(name));
        Name = name;
    }

    /// <summary>The key's name.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
}

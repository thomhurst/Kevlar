using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// A strongly typed key for storing values in <see cref="KevlarProperties"/>.
/// </summary>
/// <typeparam name="T">The type of the value stored under this key.</typeparam>
public readonly struct KevlarKey<T> : IEquatable<KevlarKey<T>>
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
    public bool Equals(KevlarKey<T> other) => StringComparer.Ordinal.Equals(Name, other.Name);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is KevlarKey<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

    /// <summary>Returns whether two keys have the same case-sensitive name.</summary>
    public static bool operator ==(KevlarKey<T> left, KevlarKey<T> right) => left.Equals(right);

    /// <summary>Returns whether two keys have different case-sensitive names.</summary>
    public static bool operator !=(KevlarKey<T> left, KevlarKey<T> right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
}

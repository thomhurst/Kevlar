namespace Kevlar.Testing;

/// <summary>How precisely inspection can bound invocations of the protected operation.</summary>
public enum AttemptBoundKind
{
    /// <summary>A conservative finite upper bound is available.</summary>
    Finite,
    /// <summary>A RetryForever sentinel supplies no useful finite attempt budget.</summary>
    Unbounded,
    /// <summary>Custom or generated behavior prevents a reliable bound.</summary>
    Indeterminate,
    /// <summary>The finite product exceeds <see cref="long.MaxValue"/>.</summary>
    Overflow,
}

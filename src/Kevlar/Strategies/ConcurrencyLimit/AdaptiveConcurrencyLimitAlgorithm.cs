namespace Kevlar;

/// <summary>The algorithm used to adjust an adaptive concurrency limit.</summary>
public enum AdaptiveConcurrencyLimitAlgorithm
{
    /// <summary>Additive increase and multiplicative decrease, evaluated once per sampling window.</summary>
    Aimd,
}

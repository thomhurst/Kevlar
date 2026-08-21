namespace Kevlar.Testing;

/// <summary>Read-only fallback configuration.</summary>
public sealed class FallbackStrategyDescriptor : StrategyDescriptor
{
    internal FallbackStrategyDescriptor(
        string description,
        Type? resultType,
        bool hasNotification)
        : base(StrategyKind.Fallback, description)
    {
        ResultType = resultType;
        HasNotification = hasNotification;
    }

    /// <summary>The fallback result type, or <see langword="null"/> for a void fallback.</summary>
    public Type? ResultType { get; }

    /// <summary>Whether this is a void fallback.</summary>
    public bool IsVoid => ResultType is null;

    /// <summary>Whether a fallback notification is configured.</summary>
    public bool HasNotification { get; }
}

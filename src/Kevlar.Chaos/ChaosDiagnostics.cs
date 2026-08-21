namespace Kevlar.Chaos;

/// <summary>Names for Kevlar.Chaos telemetry.</summary>
/// <remarks>
/// On .NET 8+ the <c>kevlar.chaos.injections</c> counter is published by a meter named
/// <see cref="MeterName"/>. Its attributes identify injection kind, shield name, operation,
/// and environment. On <c>netstandard2.0</c> the instrument is inert.
/// </remarks>
public static class ChaosDiagnostics
{
    /// <summary>Gets the name of the Kevlar.Chaos meter.</summary>
    public const string MeterName = "Kevlar.Chaos";
}

using System.Text.Json.Serialization;

namespace Kevlar.StressTests;

internal sealed record StressRunResult(
    DateTimeOffset Timestamp,
    string Commit,
    string OperatingSystem,
    string Runtime,
    int ProcessorCount,
    int Workers,
    TimeSpan TotalDuration,
    TimeSpan Warmup,
    int MeasurementRounds,
    long PeakWorkingSetBytes,
    IReadOnlyList<StressPhaseResult> Results);

[JsonSerializable(typeof(StressRunResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal sealed partial class StressJsonContext : JsonSerializerContext;

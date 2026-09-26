using System.Text.Json.Serialization;

namespace Kevlar.OutageTests;

internal sealed record OutageResult(
    int SchemaVersion, DateTimeOffset Timestamp, string Commit, string Runtime,
    string OperatingSystem, string Architecture, int ProcessorCount,
    OutageOptions Options, DependencySettings Dependency, IReadOnlyList<ScenarioResult> Scenarios);

internal sealed record DependencySettings(
    int HealthyMilliseconds, int SlowMilliseconds, int AlternateMilliseconds,
    int FailureMilliseconds, int CleanupMilliseconds, int HedgeMilliseconds,
    int BreakMilliseconds, int Concurrency, int QueueLimit);

internal sealed record ScenarioResult(
    string Name, string Pipeline, double WallSeconds, double? RecoveryMilliseconds,
    double RecoveryWindowMilliseconds, int RecoveryRequiredSuccesses,
    int PeakLogicalInFlight, int PeakDownstreamInFlight, int ActiveAfterDrain,
    int CancelledAttempts, int LosersPendingAtCallerCompletion,
    double CleanupAfterCallerP99Milliseconds, double CleanupAfterCallerMaximumMilliseconds,
    IReadOnlyList<PhaseResult> Phases);

internal sealed record PhaseResult(
    string Name, double ScheduledStartSeconds, double ScheduledEndSeconds,
    int Offered, int Submitted, int Admitted, int Succeeded, int Failed,
    int HarnessRejected, int ConcurrencyRejected, int CircuitRejected,
    int DownstreamAttempts, double AttemptsPerOfferedRequest, double AttemptsPerAdmittedRequest,
    double? LatencyP95Milliseconds, double? LatencyP99Milliseconds,
    double? SuccessLatencyP99Milliseconds, double? SchedulerLagP99Milliseconds,
    int AttemptsStartedInPhase);

[JsonSerializable(typeof(OutageResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal sealed partial class OutageJsonContext : JsonSerializerContext;

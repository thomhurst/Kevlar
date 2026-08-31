namespace Kevlar.StressTests;

internal sealed record StressPhaseResult(
    string Scenario,
    string Library,
    int Workers,
    long Operations,
    double ElapsedSeconds,
    double OperationsPerSecond,
    double CpuSeconds,
    long AllocatedBytes,
    double BytesPerOperation,
    long ManagedBytesBefore,
    long ManagedBytesAfter,
    double GcPauseSeconds,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long LockContentions);

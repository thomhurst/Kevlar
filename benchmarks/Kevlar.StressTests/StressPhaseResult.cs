namespace Kevlar.StressTests;

internal sealed record StressPhaseResult(
    string Library,
    long Operations,
    double ElapsedSeconds,
    double OperationsPerSecond,
    long AllocatedBytes,
    double BytesPerOperation,
    long ManagedBytesBefore,
    long ManagedBytesAfter,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

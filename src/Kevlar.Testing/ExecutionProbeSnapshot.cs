namespace Kevlar.Testing;

/// <summary>An immutable, versioned snapshot of execution-probe observations.</summary>
public sealed class ExecutionProbeSnapshot
{
    internal ExecutionProbeSnapshot(int attemptCount, int cancellationCount)
    {
        AttemptCount = attemptCount;
        CancellationCount = cancellationCount;
    }

    /// <summary>Gets the number of delegate invocations.</summary>
    public int AttemptCount { get; }

    /// <summary>Gets the number of active attempt tokens that requested cancellation.</summary>
    public int CancellationCount { get; }
}

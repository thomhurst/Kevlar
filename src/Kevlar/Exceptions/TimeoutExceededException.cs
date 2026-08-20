namespace Kevlar;

/// <summary>Thrown when a timeout strategy cancels an execution that exceeded its allotted time.</summary>
public sealed class TimeoutExceededException : KevlarException
{
    /// <summary>Initializes the exception for the given timeout.</summary>
    public TimeoutExceededException(TimeSpan timeout)
        : base($"The execution did not complete within the timeout of {timeout.TotalSeconds:0.###}s.")
        => Timeout = timeout;

    /// <summary>The timeout that was exceeded.</summary>
    public TimeSpan Timeout { get; }
}

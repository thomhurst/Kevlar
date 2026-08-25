namespace Kevlar;

/// <summary>Thrown when a timeout strategy cancels an execution that exceeded its allotted time.</summary>
public sealed class TimeoutExceededException : ExecutionRejectedException
{
    private const string DefaultMessage = "The execution exceeded its allotted timeout.";

    /// <summary>Initializes a timeout rejection without a recorded timeout.</summary>
    public TimeoutExceededException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Initializes a timeout rejection with a custom message.</summary>
    public TimeoutExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a timeout rejection with a custom message and cause.</summary>
    public TimeoutExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes the exception for the given timeout.</summary>
    public TimeoutExceededException(TimeSpan timeout)
        : base(FormatMessage(timeout))
        => Timeout = timeout;

    /// <summary>Initializes the exception for the given timeout and triggering cancellation.</summary>
    public TimeoutExceededException(TimeSpan timeout, OperationCanceledException innerException)
        : base(FormatMessage(timeout), innerException)
        => Timeout = timeout;

    /// <summary>The timeout that was exceeded.</summary>
    public TimeSpan Timeout { get; }

    private static string FormatMessage(TimeSpan timeout) =>
        $"The execution did not complete within the timeout of {timeout.TotalSeconds:0.###}s.";
}

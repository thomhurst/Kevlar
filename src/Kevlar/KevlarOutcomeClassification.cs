namespace Kevlar;

/// <summary>Safe, bounded classification of an event's outcome.</summary>
public enum KevlarOutcomeClassification
{
    /// <summary>The event has no outcome.</summary>
    None,

    /// <summary>The execution produced a result.</summary>
    Success,

    /// <summary>The execution failed with an exception.</summary>
    Failure,

    /// <summary>The execution was canceled.</summary>
    Canceled,
}

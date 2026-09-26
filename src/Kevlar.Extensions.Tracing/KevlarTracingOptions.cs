namespace Kevlar.Extensions.Tracing;

/// <summary>Controls the data copied into activity events by <see cref="KevlarTracing.Listen"/>.</summary>
public sealed class KevlarTracingOptions
{
    /// <summary>Gets or sets whether to include exception messages and stack traces. Defaults to false.</summary>
    /// <remarks>Exception types are always included. Messages and stack traces may contain sensitive data.</remarks>
    public bool IncludeExceptionDetails { get; set; }

    /// <summary>Gets or sets whether to include the logical operation key. Defaults to false.</summary>
    /// <remarks>Enable only when operation keys use a bounded, non-sensitive vocabulary.</remarks>
    public bool IncludeOperationKey { get; set; }

    /// <summary>Gets or sets the maximum number of UTF-16 code units in each string tag value. Defaults to 256.</summary>
    /// <remarks>Must be positive. Truncation never splits a surrogate pair.</remarks>
    public int MaximumTagValueLength { get; set; } = 256;
}

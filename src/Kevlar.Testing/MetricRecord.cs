namespace Kevlar.Testing;

/// <summary>An immutable snapshot of one Kevlar metric measurement.</summary>
public sealed class MetricRecord
{
    internal MetricRecord(
        long sequence,
        string instrumentName,
        double value,
        IReadOnlyDictionary<string, object?> tags)
    {
        Sequence = sequence;
        InstrumentName = instrumentName;
        Value = value;
        Tags = tags;
    }

    /// <summary>The recorder-wide sequence number.</summary>
    public long Sequence { get; }

    /// <summary>The documented Kevlar instrument name.</summary>
    public string InstrumentName { get; }

    /// <summary>The numeric measurement converted to <see cref="double"/>.</summary>
    public double Value { get; }

    /// <summary>A copied snapshot of the measurement's low-cardinality tags.</summary>
    public IReadOnlyDictionary<string, object?> Tags { get; }

    internal MetricRecord WithSequence(long sequence) =>
        new(sequence, InstrumentName, Value, Tags);
}

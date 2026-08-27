namespace Kevlar;

/// <summary>Provides callback-scoped information for a Kevlar metric measurement.</summary>
/// <remarks>
/// The context and its tag collection are valid only for the duration of
/// <see cref="KevlarMetricEnricher.Enrich(in KevlarMetricEnrichmentContext)"/>.
/// </remarks>
public readonly struct KevlarMetricEnrichmentContext
{
    internal KevlarMetricEnrichmentContext(
        string instrumentName,
        KevlarContext? context,
        IList<KeyValuePair<string, object?>> tags)
    {
        InstrumentName = instrumentName;
        Context = context;
        Tags = tags;
    }

    /// <summary>Gets the metric instrument name.</summary>
    public string InstrumentName { get; }

    /// <summary>
    /// Gets the active execution context, or <see langword="null"/> for measurements outside an
    /// execution such as state collection, circuit transitions, and partition evictions.
    /// </summary>
    public KevlarContext? Context { get; }

    /// <summary>Gets the mutable collection containing built-in and application-defined tags.</summary>
    public IList<KeyValuePair<string, object?>> Tags { get; }
}

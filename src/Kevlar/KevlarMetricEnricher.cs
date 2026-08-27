namespace Kevlar;

/// <summary>Adds application-defined tags to Kevlar metric measurements.</summary>
public abstract class KevlarMetricEnricher
{
    /// <summary>Adds tags to a metric measurement before it is published.</summary>
    /// <param name="context">The callback-scoped metric context and mutable tag collection.</param>
    public abstract void Enrich(in KevlarMetricEnrichmentContext context);
}

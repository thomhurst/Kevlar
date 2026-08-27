#if NET8_0_OR_GREATER
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
#endif

namespace Kevlar.Internal;

internal static class KevlarMetricEnrichment
{
    private static readonly Lock Sync = new();
    private static KevlarMetricEnricher[] _enrichers = [];

    public static IDisposable Subscribe(KevlarMetricEnricher enricher)
    {
        lock (Sync)
        {
            var current = _enrichers;
            var updated = new KevlarMetricEnricher[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[^1] = enricher;
            Volatile.Write(ref _enrichers, updated);
        }

        return new Subscription(enricher);
    }

#if NET8_0_OR_GREATER
    public static void Add(
        Counter<long> instrument,
        long value,
        in TagList tags,
        KevlarContext? context = null)
    {
        var enrichers = Volatile.Read(ref _enrichers);
        if (enrichers.Length == 0)
        {
            instrument.Add(value, tags);
            return;
        }

        var enrichedTags = Enrich(instrument.Name, context, in tags, enrichers);
        try
        {
            instrument.Add(value, CollectionsMarshal.AsSpan(enrichedTags));
        }
        finally
        {
            TagPool.Return(enrichedTags);
        }
    }

    public static void Record(
        Histogram<double> instrument,
        double value,
        in TagList tags,
        KevlarContext? context = null)
    {
        var enrichers = Volatile.Read(ref _enrichers);
        if (enrichers.Length == 0)
        {
            instrument.Record(value, tags);
            return;
        }

        var enrichedTags = Enrich(instrument.Name, context, in tags, enrichers);
        try
        {
            instrument.Record(value, CollectionsMarshal.AsSpan(enrichedTags));
        }
        finally
        {
            TagPool.Return(enrichedTags);
        }
    }

    public static Measurement<long> Measure(
        ObservableGauge<long> instrument,
        long value,
        in TagList tags)
    {
        var enrichers = Volatile.Read(ref _enrichers);
        if (enrichers.Length == 0)
        {
            return new Measurement<long>(value, tags);
        }

        var enrichedTags = Enrich(instrument.Name, context: null, in tags, enrichers);
        try
        {
            return new Measurement<long>(value, CollectionsMarshal.AsSpan(enrichedTags));
        }
        finally
        {
            TagPool.Return(enrichedTags);
        }
    }

    private static List<KeyValuePair<string, object?>> Enrich(
        string instrumentName,
        KevlarContext? context,
        in TagList tags,
        KevlarMetricEnricher[] enrichers)
    {
        var enrichedTags = TagPool.Rent();
        foreach (var tag in tags)
        {
            enrichedTags.Add(tag);
        }

        var enrichmentContext = new KevlarMetricEnrichmentContext(
            instrumentName,
            context,
            enrichedTags);
        foreach (var enricher in enrichers)
        {
            try
            {
                enricher.Enrich(in enrichmentContext);
            }
            catch
            {
                // Metric enrichment is diagnostic and cannot affect execution or other enrichers.
            }
        }

        return enrichedTags;
    }

    private static class TagPool
    {
        private const int MaximumRetainedCapacity = 64;
        private static readonly ConcurrentBag<List<KeyValuePair<string, object?>>> Tags = [];

        public static List<KeyValuePair<string, object?>> Rent() =>
            Tags.TryTake(out var tags) ? tags : new List<KeyValuePair<string, object?>>(8);

        public static void Return(List<KeyValuePair<string, object?>> tags)
        {
            if (tags.Capacity > MaximumRetainedCapacity)
            {
                return;
            }

            tags.Clear();
            Tags.Add(tags);
        }
    }
#endif

    private static void Unsubscribe(KevlarMetricEnricher enricher)
    {
        lock (Sync)
        {
            var current = _enrichers;
            var index = -1;
            for (var candidate = 0; candidate < current.Length; candidate++)
            {
                if (ReferenceEquals(current[candidate], enricher))
                {
                    index = candidate;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            var updated = new KevlarMetricEnricher[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            Volatile.Write(ref _enrichers, updated);
        }
    }

    private sealed class Subscription(KevlarMetricEnricher enricher) : IDisposable
    {
        private KevlarMetricEnricher? _enricher = enricher;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _enricher, null);
            if (current is not null)
            {
                Unsubscribe(current);
            }
        }
    }
}

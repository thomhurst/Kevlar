using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.RegularExpressions;
using Kevlar.Chaos;

namespace Kevlar.Chaos.Tests;

[NotInParallel]
public class DocsConsistencyTests
{
    [Test]
    public async Task Observability_Instrument_Table_Matches_Published_Instruments()
    {
        using var observer = new InstrumentObserver();

        await ExerciseEveryInstrumentAsync();

        var documented = ParseInstrumentTable();
        var available = documented
            .Where(static row => IsAvailableOnCurrentTarget(row.Value.MinimumTarget))
            .ToDictionary(StringComparer.Ordinal);
        await Assert.That(available.Keys).IsEquivalentTo(observer.Instruments.Keys);

        foreach (var (name, instrument) in observer.Instruments)
        {
            var row = available[name];
            await Assert.That(row.Type).IsEqualTo(InstrumentType(instrument));
            await Assert.That(row.Unit).IsEqualTo(instrument.Unit);
            await Assert.That(row.MinimumTarget).IsEqualTo(MinimumTarget(instrument));
            await Assert.That(row.Tags).IsEquivalentTo(observer.Tags[name].Keys);
        }
    }

    [Test]
    public async Task Observability_Callbacks_Section_Lists_Every_Strategy()
    {
        var documented = ParseCallbackTable();
        var reflected = PublicCallbackContract();

        await Assert.That(documented.Keys).IsEquivalentTo(reflected.Keys);
        foreach (var (strategy, callbacks) in reflected)
        {
            await Assert.That(documented[strategy]).IsEquivalentTo(callbacks);
        }
    }

    private static async Task ExerciseEveryInstrumentAsync()
    {
        var retryAttempts = 0;
        await Shield.Retry(1, Backoff.None)
            .WithName("docs-retry")
            .ExecuteAsync<int>(_ =>
            {
                if (Interlocked.Increment(ref retryAttempts) == 1)
                {
                    throw new InvalidOperationException();
                }

                return new ValueTask<int>(1);
            });

        _ = await Shield.Timeout(TimeSpan.FromMilliseconds(10))
            .WithName("docs-timeout")
            .ExecuteOutcomeAsync<int>(async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 1;
            });

        await Shield.Hedge(1, TimeSpan.Zero)
            .WithName("docs-hedge")
            .ExecuteAsync(_ => new ValueTask<int>(1));

        await Shield.For<int>()
            .FallbackTo(42)
            .WithName("docs-fallback")
            .ExecuteAsync<int>(_ => throw new InvalidOperationException());

        var circuit = Shield.CircuitBreaker(1, TimeSpan.FromMinutes(1))
            .WithName("docs-circuit");
        _ = await circuit.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        _ = await circuit.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));

        await Shield.ConcurrencyLimit(1)
            .WithName("docs-concurrency")
            .ExecuteAsync(_ => ValueTask.CompletedTask);

        var rateLimit = Shield.RateLimit(1, TimeSpan.FromMinutes(1))
            .WithName("docs-rate");
        await rateLimit.ExecuteAsync(_ => ValueTask.CompletedTask);
        _ = await rateLimit.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));

        using (ChaosScope.Begin("docs-operation", "docs-environment"))
        {
            _ = await ChaosShield.Fault(static options => options.Enabled = true)
                .WithName("docs-chaos")
                .ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        }
    }

    private static Dictionary<string, InstrumentRow> ParseInstrumentTable()
    {
        var rows = new Dictionary<string, InstrumentRow>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(ObservabilityPath()))
        {
            if (!line.StartsWith("| `kevlar.", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|').Select(static cell => cell.Trim()).ToArray();
            if (cells.Length < 8)
            {
                throw new InvalidOperationException(
                    "The observability instrument table must include type, unit, minimum target, measures, and attributes columns.");
            }

            var name = cells[1].Trim('`');
            var tags = Regex.Matches(cells[6], "`(kevlar\\.[^`]+)`")
                .Select(static match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
            rows.Add(name, new InstrumentRow(
                cells[2],
                cells[3].Trim('`'),
                cells[4].Trim('`'),
                tags));
        }

        return rows;
    }

    private static Dictionary<string, HashSet<string>> ParseCallbackTable()
    {
        var rows = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var inCallbacks = false;
        foreach (var line in File.ReadLines(ObservabilityPath()))
        {
            if (line == "## Callbacks")
            {
                inCallbacks = true;
                continue;
            }

            if (inCallbacks && line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (!inCallbacks || !line.StartsWith("| ", StringComparison.Ordinal)
                || line.StartsWith("| Strategy ", StringComparison.Ordinal)
                || line.StartsWith("|---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|').Select(static cell => cell.Trim()).ToArray();
            var callbacks = Regex.Matches(line, "`(On[A-Za-z]+)`")
                .Select(static match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
            rows.Add(cells[1], callbacks);
        }

        return rows;
    }

    private static Dictionary<string, HashSet<string>> PublicCallbackContract()
    {
        var optionTypes = new[] { typeof(Shield).Assembly, typeof(ChaosShield).Assembly }
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type => IsStrategyOptions(type));

        return optionTypes
            .GroupBy(StrategyName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .SelectMany(static type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    .Select(static property => property.Name)
                    .Where(static name => name.StartsWith("On", StringComparison.Ordinal))
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static bool IsStrategyOptions(Type type) =>
        typeof(ChaosOptions).IsAssignableFrom(type)
        || type.Name.StartsWith("RetryOptions", StringComparison.Ordinal)
        || type.Name.StartsWith("CircuitBreakerOptions", StringComparison.Ordinal)
        || type.Name.StartsWith("TimeoutOptions", StringComparison.Ordinal)
        || type.Name.StartsWith("HedgeOptions", StringComparison.Ordinal)
        || type.Name.StartsWith("FallbackOptions", StringComparison.Ordinal)
        || type.Name.StartsWith("ConcurrencyLimitOptions", StringComparison.Ordinal)
        || type.Name.StartsWith("RateLimitOptions", StringComparison.Ordinal);

    private static string StrategyName(Type type)
    {
        if (typeof(ChaosOptions).IsAssignableFrom(type))
        {
            return "Chaos";
        }

        if (type.Name.StartsWith("CircuitBreaker", StringComparison.Ordinal))
        {
            return "Circuit breaker";
        }

        if (type.Name.StartsWith("ConcurrencyLimit", StringComparison.Ordinal))
        {
            return "Concurrency limit";
        }

        if (type.Name.StartsWith("RateLimit", StringComparison.Ordinal))
        {
            return "Rate limit";
        }

        if (type.Name.StartsWith("Hedge", StringComparison.Ordinal))
        {
            return "Hedging";
        }

        var marker = type.Name.IndexOf("Options", StringComparison.Ordinal);
        return type.Name[..marker];
    }

    private static string InstrumentType(Instrument instrument) => instrument switch
    {
        Counter<long> => "Counter",
        Histogram<double> => "Histogram",
#if NET9_0_OR_GREATER
        Gauge<long> => "Gauge",
#endif
        _ => throw new InvalidOperationException($"Unsupported instrument type {instrument.GetType()}"),
    };

    private static string MinimumTarget(Instrument instrument) =>
        instrument is Counter<long> or Histogram<double> ? "net8.0" : "net10.0";

    private static bool IsAvailableOnCurrentTarget(string minimumTarget) =>
#if NET10_0_OR_GREATER
        minimumTarget is "net8.0" or "net10.0";
#else
        minimumTarget == "net8.0";
#endif

    private static string ObservabilityPath() => Path.Combine(
        RepositoryRoot(),
        "docs",
        "docs",
        "observability.md");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kevlar.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Kevlar repository root.");
    }

    private sealed record InstrumentRow(
        string Type,
        string Unit,
        string MinimumTarget,
        HashSet<string> Tags);

    private sealed class InstrumentObserver : IDisposable
    {
        private readonly MeterListener _listener = new();

        public InstrumentObserver()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name is KevlarDiagnostics.MeterName or ChaosDiagnostics.MeterName)
                {
                    Instruments[instrument.Name] = instrument;
                    Tags.TryAdd(instrument.Name, new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => CaptureTags(instrument, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => CaptureTags(instrument, tags));
            _listener.Start();
        }

        public ConcurrentDictionary<string, Instrument> Instruments { get; } =
            new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> Tags { get; } =
            new(StringComparer.Ordinal);

        public void Dispose() => _listener.Dispose();

        private void CaptureTags(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var keys = Tags[instrument.Name];
            foreach (var tag in tags)
            {
                keys.TryAdd(tag.Key, 0);
            }
        }
    }
}

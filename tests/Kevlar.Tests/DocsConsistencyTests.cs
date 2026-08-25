using System.Reflection;
using System.Runtime.CompilerServices;
using Kevlar.Chaos;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Grpc;
using Kevlar.Extensions.Http;
using Kevlar.Extensions.RateLimiting;
using Kevlar.Internal;
using Kevlar.Testing;

namespace Kevlar.Tests;

public class DocsConsistencyTests
{
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(Shield).Assembly,
        typeof(Kevlar.Analyzers.PipelineHazardAnalyzer).Assembly,
        typeof(ChaosShield).Assembly,
        typeof(KevlarServiceCollectionExtensions).Assembly,
        typeof(GrpcShield).Assembly,
        typeof(HttpShield).Assembly,
        typeof(ShieldRateLimiterExtensions).Assembly,
        typeof(TelemetryRecorder).Assembly,
    ];

    [Test]
    public async Task Exceptions_Page_Lists_Every_Public_Exception()
    {
        var rows = ReadExceptionRows();
        var exceptions = ShippedAssemblies
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(static type => typeof(Exception).IsAssignableFrom(type))
            .OrderBy(static type => type.Name)
            .ToArray();

        await Assert.That(rows.Keys).IsEquivalentTo(exceptions.Select(static type => type.Name));

        foreach (var exception in exceptions)
        {
            var row = rows[exception.Name];
            await Assert.That(row.BaseClass).IsEqualTo(exception.BaseType!.Name);

            foreach (var property in exception.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                await Assert.That(row.Properties).Contains(property.Name);
            }
        }
    }

    [Test]
    public async Task Exceptions_Page_Default_Clause_Column_Matches_OutcomeJudge()
    {
        var rows = ReadExceptionRows();
        var exceptions = new Exception[]
        {
            new TimeoutExceededException(TimeSpan.FromSeconds(1)),
            new CircuitOpenException(TimeSpan.FromSeconds(1), false, new InvalidOperationException()),
            new ConcurrencyLimitExceededException(),
            new RateLimitExceededException(TimeSpan.FromSeconds(1)),
            new ChaosInjectedException(),
            new HttpRequestReplayException("Replay failed."),
            new ShieldAssertionException("Assertion failed."),
        };

        foreach (var exception in exceptions)
        {
            var outcome = Outcome<int>.FromException(exception);
            var expected = OutcomeJudge.Default.ShouldHandle(in outcome) ? "Yes" : "No";
            await Assert.That(rows[exception.GetType().Name].DefaultClause).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Migration_Guide_Mentions_Every_Public_Extension_Entry_Point()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrationGuide = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "docs", "docs", "polly-migration.md"));
        var extensionNames = ShippedAssemblies
            .Where(static assembly => assembly.GetName().Name?.StartsWith(
                "Kevlar.Extensions.",
                StringComparison.Ordinal) is true)
            .SelectMany(static assembly => assembly.ExportedTypes)
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(static method => method.IsDefined(typeof(ExtensionAttribute), inherit: false))
            .Select(static method => method.Name)
            .Where(static name =>
                name.StartsWith("Add", StringComparison.Ordinal) ||
                name.StartsWith("With", StringComparison.Ordinal) ||
                name.StartsWith("Use", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var extensionName in extensionNames)
        {
            await Assert.That(migrationGuide).Contains(extensionName);
        }
    }

    private static Dictionary<string, ExceptionDocRow> ReadExceptionRows()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "docs", "docs", "exceptions.md");
        return File.ReadLines(path)
            .Where(static line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(static line => line.Split('|', StringSplitOptions.TrimEntries))
            .ToDictionary(
                static cells => cells[1].Trim('`'),
                static cells => new ExceptionDocRow(
                    cells[3],
                    cells[4].Trim('`'),
                    cells[6]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kevlar.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Kevlar repository root.");
    }

    private sealed record ExceptionDocRow(string Properties, string BaseClass, string DefaultClause);
}

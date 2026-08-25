using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
    public async Task Package_Table_Lists_Every_Packable_Project()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packagePattern = new System.Text.RegularExpressions.Regex(
            @"^\| \[`(?<package>Kevlar(?:\.[^`]+)?)`\]\(https://www\.nuget\.org/packages/",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var documentedPackages = File.ReadLines(Path.Combine(repositoryRoot, "README.md"))
            .Select(line => packagePattern.Match(line))
            .Where(static match => match.Success)
            .Select(static match => match.Groups["package"].Value)
            .OrderBy(static package => package, StringComparer.Ordinal)
            .ToArray();
        var packableProjects = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(static path => IsPackableProject(XDocument.Load(path)))
            .Select(static path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(static package => package, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(documentedPackages).IsEquivalentTo(packableProjects);
    }

    [Test]
    public async Task Support_Policy_TFM_Table_Matches_Packable_Projects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projects = ReadPackableProjectTargets(repositoryRoot);
        var documented = ReadSupportTargets(repositoryRoot);

        await Assert.That(documented.Keys).IsEquivalentTo(projects.Keys);
        foreach (var (package, targets) in projects)
        {
            await Assert.That(documented[package]).IsEquivalentTo(targets);
        }
    }

    [Test]
    public async Task Support_Policy_Packability_Defaults_To_True()
    {
        var defaultProject = XDocument.Parse("<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var disabledProject = XDocument.Parse(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
            </Project>
            """);

        await Assert.That(IsPackableProject(defaultProject)).IsTrue();
        await Assert.That(IsPackableProject(disabledProject)).IsFalse();
    }

    [Test]
    public async Task Thread_Safety_Page_Mentions_Every_Public_Stateful_Contract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var documentation = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "docs", "thread-safety.md"));
        var statefulTypes = ShippedAssemblies
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(static type => type.IsClass && !type.IsAbstract)
            .Where(static type => type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Any(static field => !field.IsInitOnly && !field.IsLiteral))
            .Select(DocumentationTypeName)
            .Concat(new[]
            {
                typeof(Shield),
                typeof(Shield<>),
                typeof(ShieldBuilder),
                typeof(ShieldBuilder<>),
                typeof(PartitionedShield<>),
                typeof(PartitionedShield<,>),
                typeof(Strategy),
                typeof(IKevlarRegistry),
                typeof(IShieldProvider),
            }.Select(DocumentationTypeName))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var missingTypes = statefulTypes
            .Where(type => !documentation.Contains($"`{type}`", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(missingTypes).IsEmpty();
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

    private static Dictionary<string, HashSet<string>> ReadSupportTargets(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "docs", "docs", "support-policy.md");
        var rows = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var insideTable = false;
        foreach (var line in File.ReadLines(path))
        {
            if (line == "<!-- supported-tfms:start -->")
            {
                insideTable = true;
                continue;
            }

            if (line == "<!-- supported-tfms:end -->")
            {
                break;
            }

            if (!insideTable || !line.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            var package = cells[1].Trim('`');
            var targets = Regex.Matches(cells[2], "`([^`]+)`")
                .Select(static match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
            rows.Add(package, targets);
        }

        return rows;
    }

    private static Dictionary<string, HashSet<string>> ReadPackableProjectTargets(string repositoryRoot) =>
        Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(static path => (Path: path, Project: XDocument.Load(path)))
            .Where(static item => IsPackableProject(item.Project))
            .ToDictionary(
                static item => Path.GetFileNameWithoutExtension(item.Path),
                static item => item.Project
                    .Descendants()
                    .Where(static element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                    .SelectMany(static element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static bool IsPackableProject(XDocument project) =>
        !string.Equals(
            project.Descendants("IsPackable").FirstOrDefault()?.Value,
            "false",
            StringComparison.OrdinalIgnoreCase);

    private static string DocumentationTypeName(Type type)
    {
        var marker = type.Name.IndexOf('`');
        if (marker < 0)
        {
            return type.Name;
        }

        var arguments = string.Join(", ", type.GetGenericArguments().Select(static argument => argument.Name));
        return $"{type.Name[..marker]}<{arguments}>";
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

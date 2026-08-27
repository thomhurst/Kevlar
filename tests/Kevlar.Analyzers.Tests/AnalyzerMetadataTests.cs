using Kevlar.Analyzers;
using Microsoft.CodeAnalysis;

namespace Kevlar.Analyzers.Tests;

public class AnalyzerMetadataTests
{
    [Test]
    public async Task Compiler_Analyzer_Assembly_Does_Not_Reference_Workspaces()
    {
        var references = typeof(PipelineHazardAnalyzer).Assembly.GetReferencedAssemblies();

        await Assert.That(references.Any(static reference =>
            reference.Name?.Contains("Workspaces", StringComparison.Ordinal) == true)).IsFalse();
    }

    [Test]
    public async Task Supported_Diagnostics_Expose_Every_Public_Rule_Exactly_Once()
    {
        var cancellationRules = new IgnoredCancellationTokenAnalyzer().SupportedDiagnostics;
        var pipelineRules = new PipelineHazardAnalyzer().SupportedDiagnostics;

        await Assert.That(cancellationRules.Select(static rule => rule.Id).SequenceEqual(["KEV001"]))
            .IsTrue();
        await Assert.That(pipelineRules.Select(static rule => rule.Id).SequenceEqual(
            ["KEV002", "KEV003", "KEV004", "KEV005", "KEV006", "KEV007", "KEV008", "KEV009", "KEV010", "KEV011", "KEV012", "KEV014"]))
            .IsTrue();

        var allRules = cancellationRules.Concat(pipelineRules).ToArray();
        await Assert.That(allRules.Select(static rule => rule.Id).Distinct().Count())
            .IsEqualTo(allRules.Length);
    }

    [Test]
    public async Task Diagnostic_Metadata_Preserves_Severity_Category_And_Descriptions()
    {
        var rules = new IgnoredCancellationTokenAnalyzer().SupportedDiagnostics
            .Concat(new PipelineHazardAnalyzer().SupportedDiagnostics)
            .ToDictionary(static rule => rule.Id, StringComparer.Ordinal);

        foreach (var reliabilityRule in new[] { "KEV001", "KEV002", "KEV004", "KEV006", "KEV012", "KEV014" })
        {
            await Assert.That(rules[reliabilityRule].Category).IsEqualTo("Reliability");
            await Assert.That(rules[reliabilityRule].DefaultSeverity).IsEqualTo(DiagnosticSeverity.Warning);
        }

        foreach (var configurationRule in new[] { "KEV003", "KEV005", "KEV007", "KEV008" })
        {
            await Assert.That(rules[configurationRule].Category).IsEqualTo("Configuration");
            await Assert.That(rules[configurationRule].DefaultSeverity).IsEqualTo(DiagnosticSeverity.Warning);
        }

        foreach (var informationalRule in new[] { "KEV009", "KEV010", "KEV011" })
        {
            await Assert.That(rules[informationalRule].Category).IsEqualTo("Configuration");
            await Assert.That(rules[informationalRule].DefaultSeverity).IsEqualTo(DiagnosticSeverity.Info);
        }

        await Assert.That(rules.Values.All(static rule => rule.IsEnabledByDefault)).IsTrue();
        await Assert.That(rules.Values.All(static rule =>
            !string.IsNullOrWhiteSpace(rule.Title.ToString())
            && !string.IsNullOrWhiteSpace(rule.MessageFormat.ToString())
            && !string.IsNullOrWhiteSpace(rule.Description.ToString()))).IsTrue();
    }

    [Test]
    public async Task Analyzer_Help_Links_Target_Published_Documentation_Anchors()
    {
        const string helpBase = "https://thomhurst.github.io/Kevlar/docs/analyzers#";
        var analyzerDocsPath = Path.Combine(FindRepositoryRoot(), "docs", "docs", "analyzers.md");
        var anchorsByRuleId = File.ReadLines(analyzerDocsPath)
            .Where(static line => line.StartsWith("## KEV", StringComparison.Ordinal))
            .ToDictionary(
                static line => line.Substring(3, 6),
                static line => line.Substring(3)
                    .ToLowerInvariant()
                    .Replace(": ", "-", StringComparison.Ordinal)
                    .Replace(' ', '-'),
                StringComparer.Ordinal);
        var rules = new IgnoredCancellationTokenAnalyzer().SupportedDiagnostics
            .Concat(new PipelineHazardAnalyzer().SupportedDiagnostics);

        foreach (var rule in rules)
        {
            await Assert.That(anchorsByRuleId.ContainsKey(rule.Id)).IsTrue();
            await Assert.That(rule.HelpLinkUri)
                .IsEqualTo(helpBase + anchorsByRuleId[rule.Id]);
        }
    }

    [Test]
    [Arguments("KEV001", "Reliability", DiagnosticSeverity.Warning)]
    [Arguments("KEV002", "Reliability", DiagnosticSeverity.Warning)]
    [Arguments("KEV003", "Configuration", DiagnosticSeverity.Warning)]
    [Arguments("KEV004", "Reliability", DiagnosticSeverity.Warning)]
    [Arguments("KEV005", "Configuration", DiagnosticSeverity.Warning)]
    [Arguments("KEV006", "Reliability", DiagnosticSeverity.Warning)]
    [Arguments("KEV007", "Configuration", DiagnosticSeverity.Warning)]
    [Arguments("KEV008", "Configuration", DiagnosticSeverity.Warning)]
    [Arguments("KEV009", "Configuration", DiagnosticSeverity.Info)]
    [Arguments("KEV010", "Configuration", DiagnosticSeverity.Info)]
    [Arguments("KEV011", "Configuration", DiagnosticSeverity.Info)]
    [Arguments("KEV012", "Reliability", DiagnosticSeverity.Warning)]
    [Arguments("KEV014", "Reliability", DiagnosticSeverity.Warning)]
    public async Task Analyzer_Releases_Shipped_Lists_Every_Rule(
        string ruleId,
        string category,
        DiagnosticSeverity severity)
    {
        var shippedPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Kevlar.Analyzers",
            "AnalyzerReleases.Shipped.md");
        var shippedRule = File.ReadLines(shippedPath)
            .Select(static line => line.Split('|', StringSplitOptions.TrimEntries))
            .Single(columns => columns.Length >= 3 && columns[0] == ruleId);
        var descriptor = new IgnoredCancellationTokenAnalyzer().SupportedDiagnostics
            .Concat(new PipelineHazardAnalyzer().SupportedDiagnostics)
            .Single(rule => rule.Id == ruleId);

        await Assert.That(shippedRule[1]).IsEqualTo(category);
        await Assert.That(shippedRule[2]).IsEqualTo(severity.ToString());
        await Assert.That(descriptor.Category).IsEqualTo(shippedRule[1]);
        await Assert.That(descriptor.DefaultSeverity.ToString()).IsEqualTo(shippedRule[2]);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kevlar.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}

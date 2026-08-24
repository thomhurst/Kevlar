using Kevlar.Analyzers;
using Microsoft.CodeAnalysis;

namespace Kevlar.Analyzers.Tests;

public class AnalyzerMetadataTests
{
    [Test]
    public async Task Supported_Diagnostics_Expose_Every_Public_Rule_Exactly_Once()
    {
        var cancellationRules = new IgnoredCancellationTokenAnalyzer().SupportedDiagnostics;
        var pipelineRules = new PipelineHazardAnalyzer().SupportedDiagnostics;

        await Assert.That(cancellationRules.Select(static rule => rule.Id).SequenceEqual(["KEV001"]))
            .IsTrue();
        await Assert.That(pipelineRules.Select(static rule => rule.Id).SequenceEqual(
            ["KEV002", "KEV003", "KEV004", "KEV006", "KEV007", "KEV008", "KEV009", "KEV010", "KEV011"]))
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

        foreach (var reliabilityRule in new[] { "KEV001", "KEV002", "KEV004", "KEV006" })
        {
            await Assert.That(rules[reliabilityRule].Category).IsEqualTo("Reliability");
            await Assert.That(rules[reliabilityRule].DefaultSeverity).IsEqualTo(DiagnosticSeverity.Warning);
        }

        foreach (var configurationRule in new[] { "KEV003", "KEV007", "KEV008" })
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
}

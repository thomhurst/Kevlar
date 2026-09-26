using System.Collections.Immutable;
using Kevlar.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kevlar.Analyzers.Tests;

public class DurationBudgetAnalyzerTests
{
    [Test]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Timeout(TimeSpan.FromSeconds(10))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Timeout(TimeSpan.FromSeconds(5))")]
    [Arguments("Shield.For<int>().Timeout(TimeSpan.FromSeconds(5)).When<Exception>().Timeout(TimeSpan.FromSeconds(6))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).When<Exception>().Retry(1, Backoff.None).Timeout(TimeSpan.FromSeconds(6))")]
    [Arguments("ShieldExtensions.Timeout(Shield.Timeout(TimeSpan.FromSeconds(5)), TimeSpan.FromSeconds(6))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Constant(TimeSpan.FromSeconds(3)))")]
    [Arguments("Shield.For<int>().Timeout(TimeSpan.FromSeconds(5)).When<Exception>().Retry(3, Backoff.Constant(TimeSpan.FromSeconds(3)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(6)).Retry(3, Backoff.Linear(TimeSpan.FromSeconds(1)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(7)).Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(1)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(1), factor: 3, maxDelay: TimeSpan.FromSeconds(2)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(backoff: Backoff.Linear(TimeSpan.FromSeconds(2), maxDelay: TimeSpan.FromSeconds(2)), maxRetries: 3)")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Constant(TimeSpan.FromSeconds(3), jitter: Jitter.Full))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(int.MaxValue, Backoff.Exponential(TimeSpan.FromTicks(1)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(int.MaxValue, Backoff.Linear(TimeSpan.FromTicks(1)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(int.MaxValue, Backoff.Constant(TimeSpan.FromSeconds(1)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromTicks(4)).Retry(3, Backoff.Exponential(TimeSpan.FromTicks(1), factor: 1.5))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Wrap(Shield.Timeout(TimeSpan.FromSeconds(2)).Timeout(TimeSpan.FromSeconds(3)))")]
    [Arguments("Shield.Compose(Shield.Empty, Shield.Timeout(TimeSpan.FromSeconds(2)).Timeout(TimeSpan.FromSeconds(3)))")]
    [Arguments("ShieldExtensions.Timeout(timeout: TimeSpan.FromSeconds(6), shield: Shield.Timeout(TimeSpan.FromSeconds(5)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromMilliseconds(1)).Retry(2, Backoff.Constant(TimeSpan.FromMicroseconds(500)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromMinutes(1)).Timeout(TimeSpan.FromHours(1))")]
    [Arguments("Shield.Timeout(TimeSpan.FromHours(1)).Timeout(TimeSpan.FromDays(1))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(2), factor: 1, maxDelay: null))")]
    [Arguments("Shield.For<int>().Timeout(TimeSpan.FromSeconds(5)).WhenResultEquals(-1).Retry(3, Backoff.Linear(TimeSpan.FromSeconds(1)))")]
    public async Task Reports_Literal_Hazards(string expression)
    {
        var diagnostics = await AnalyzeAsync(expression);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("KEV015");
        await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Timeout(TimeSpan.FromSeconds(4))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(1, Backoff.Constant(TimeSpan.FromSeconds(3)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Linear(TimeSpan.FromSeconds(3), maxDelay: TimeSpan.FromSeconds(1)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(3), maxDelay: TimeSpan.FromSeconds(1)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Exponential(TimeSpan.Zero))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(0, Backoff.Constant(TimeSpan.FromSeconds(9)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.None)")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Custom(_ => TimeSpan.FromSeconds(9)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Constant(GetDuration()))")]
    [Arguments("Shield.Timeout(GetDuration()).Timeout(TimeSpan.FromSeconds(9))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Timeout(GetDuration())")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(GetCount(), Backoff.Constant(TimeSpan.FromSeconds(9)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Linear(TimeSpan.FromSeconds(9), maxDelay: GetDuration()))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Timeout(options => options.Timeout = TimeSpan.FromSeconds(9))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(options => options.MaxRetries = 9)")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Wrap(Shield.Timeout(TimeSpan.FromSeconds(9)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Wrap(Shield.Empty).Timeout(TimeSpan.FromSeconds(9))")]
    [Arguments("Shield.Compose(Shield.Timeout(TimeSpan.FromSeconds(5)), Shield.Timeout(TimeSpan.FromSeconds(9)))")]
    [Arguments("Shield<int>.Compose(Shield.For<int>().Timeout(TimeSpan.FromSeconds(5)), Shield.For<int>().Timeout(TimeSpan.FromSeconds(9)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromTicks(5)).Retry(3, Backoff.Exponential(TimeSpan.FromTicks(1), factor: 1.4))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Constant(TimeSpan.FromSeconds(double.NaN)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(1), factor: double.PositiveInfinity))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Exponential(TimeSpan.FromSeconds(2), maxDelay: TimeSpan.Zero))")]
    [Arguments("Shield.For<int>().Timeout(TimeSpan.FromSeconds(5)).Wrap(Shield.For<int>()).Timeout(TimeSpan.FromSeconds(9))")]
    [Arguments("Shield.Compose(Shield.Timeout(TimeSpan.FromSeconds(5)), Shield.Empty).Retry(3, Backoff.Constant(TimeSpan.FromSeconds(3)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromDays(49)).Retry(1, Backoff.Linear(TimeSpan.FromDays(48)))")]
    [Arguments("Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(-1, Backoff.Constant(TimeSpan.FromSeconds(9)))")]
    [Arguments("Shield.Timeout(TimeSpan.Zero).Timeout(TimeSpan.FromSeconds(9))")]
    [Arguments("new Other().Timeout(TimeSpan.FromSeconds(5)).Timeout(TimeSpan.FromSeconds(9))")]
    public async Task Skips_Safe_Or_Unknown_Budgets(string expression)
    {
        await Assert.That(await AnalyzeAsync(expression)).IsEmpty();
    }

    [Test]
    public async Task Reports_Both_Hazards_In_The_Same_Chain()
    {
        var diagnostics = await AnalyzeAsync(
            "Shield.Timeout(TimeSpan.FromSeconds(5)).Retry(3, Backoff.Constant(TimeSpan.FromSeconds(3))).Timeout(TimeSpan.FromSeconds(10))");

        await Assert.That(diagnostics.Length).IsEqualTo(2);
        await Assert.That(diagnostics.All(diagnostic => diagnostic.Id == "KEV015")).IsTrue();
    }

    [Test]
    public async Task Recognizes_Numeric_Constants_And_TimeSpan_Units()
    {
        var diagnostics = await AnalyzeAsync(
            "Shield.Timeout(TimeSpan.FromMilliseconds(Budget)).Retry(Attempts, Backoff.Constant(TimeSpan.FromSeconds(Delay)))",
            "const int Budget = 6000; const int Attempts = 2; const double Delay = 3;");
        await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Ignores_Runtime_Locals_And_Generated_Code()
    {
        await Assert.That(await AnalyzeAsync(
            "Shield.Timeout(budget).Timeout(TimeSpan.FromSeconds(10))",
            "var budget = TimeSpan.FromSeconds(5);")).IsEmpty();
        await Assert.That(await AnalyzeAsync(
            "Shield.Timeout(TimeSpan.FromSeconds(5)).Timeout(TimeSpan.FromSeconds(10))",
            generated: true)).IsEmpty();
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string expression, string setup = "", bool generated = false)
    {
        var source = (generated ? "// <auto-generated/>\n" : "") + $$"""
            using System;
            using Kevlar;
            public class Subject
            {
                sealed class Other { public Other Timeout(TimeSpan timeout) => this; }
                static TimeSpan GetDuration() => TimeSpan.FromSeconds(9);
                static int GetCount() => 3;
                public void Run()
                {
                    {{setup}}
                    _ = {{expression}};
                }
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location));
        var compilation = CSharpCompilation.Create("DurationBudgetTest",
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors.Select(d => d.ToString())));
        }

        return await compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new DurationBudgetAnalyzer())).GetAnalyzerDiagnosticsAsync();
    }
}

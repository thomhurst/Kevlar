using System.Collections.Immutable;
using Kevlar.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kevlar.Analyzers.Tests;

public class PipelineHazardAnalyzerTests
{
    [Test]
    public async Task KEV002_Flags_Known_Hedging_Shields_Used_Synchronously()
    {
        var cases = new[]
        {
            "_ = Shield.Hedge(2, TimeSpan.Zero).Execute(_ => 1);",
            "Shield.Empty.Hedge(2, TimeSpan.Zero).Execute(_ => { });",
            "_ = Shield.For<int>().Hedge(2, TimeSpan.Zero).Execute(_ => 1);",
            "var shield = Shield.Hedge(2, TimeSpan.Zero); var alias = shield; _ = alias.Execute(_ => 1);",
            "Shield? shield = Shield.Hedge(2, TimeSpan.Zero); _ = shield?.Execute(_ => 1);",
            "var shield = ShieldExtensions.Hedge(Shield.Empty, 2, TimeSpan.Zero); _ = shield.Execute(_ => 1);",
            "_ = Shield.Empty.Wrap(Shield.Hedge(2, TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield.Compose(Shield.Empty, Shield.Hedge(2, TimeSpan.Zero)).Execute(_ => 1);",
            "_ = Shield<int>.Empty.Wrap(Shield.Hedge(2, TimeSpan.Zero)).Execute(_ => 1);",
        };

        await AssertEachAsync(cases, "KEV002");
    }

    [Test]
    public async Task KEV002_Supports_Type_Aliases_And_Generic_Result_Shields()
    {
        var aliasDiagnostics = await AnalyzeSourceAsync("""
            using KShield = Kevlar.Shield;

            public class TestSubject
            {
                public int Run() => KShield.Hedge(2, TimeSpan.Zero).Execute(_ => 1);
            }
            """);
        var genericDiagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public T Run<T>() => Shield.For<T>().Hedge(2, TimeSpan.Zero).Execute(_ => default!);
            }
            """);

        await AssertRuleAsync(aliasDiagnostics, "KEV002");
        await AssertRuleAsync(genericDiagnostics, "KEV002");
    }

    [Test]
    public async Task KEV002_Skips_Async_Unknown_And_Reassigned_Shields()
    {
        var cases = new[]
        {
            "_ = Shield.Hedge(2, TimeSpan.Zero).ExecuteAsync(_ => new ValueTask<int>(1));",
            "var shield = CreateShield(); _ = shield.Execute(_ => 1);",
            "var shield = Shield.Hedge(2, TimeSpan.Zero); shield = Shield.Empty; _ = shield.Execute(_ => 1);",
            "_ = Shield.Empty.Execute(_ => 1);",
            "_ = Shield.Hedge(1, TimeSpan.Zero).Execute(_ => 1);",
            "var attempts = DateTime.Now.Day; _ = Shield.Hedge(attempts, TimeSpan.Zero).Execute(_ => 1);",
            "_ = Shield.Hedge(options => options.MaxAttempts = 2).Execute(_ => 1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, "private static Shield CreateShield() => Shield.Hedge(2, TimeSpan.Zero);");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV003_Flags_Unreachable_Reactive_Strategies()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().Retry(1).Fallback(0);",
            "_ = Shield.For<int>().Hedge(2, TimeSpan.Zero).Fallback(0);",
            "_ = Shield.For<int>().CircuitBreaker(2, TimeSpan.FromSeconds(1)).Fallback(0);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1).Fallback(0);",
            "var shield = Shield.For<int>().Retry(1); var alias = shield; _ = alias.Fallback(0);",
            "Shield<int>? shield = Shield.For<int>().Retry(1); _ = shield?.Fallback(0);",
            "_ = ShieldExtensions.Fallback(ShieldExtensions.Retry(Shield.Empty, 1), static _ => ValueTask.CompletedTask);",
        };

        await AssertEachAsync(cases, "KEV003");
    }

    [Test]
    public async Task KEV003_Supports_Type_Aliases_And_Generic_Result_Shields()
    {
        var aliasDiagnostics = await AnalyzeSourceAsync("""
            using KShield = Kevlar.Shield;

            public class TestSubject
            {
                public Shield<int> Build() => KShield.For<int>().Retry(1).Fallback(0);
            }
            """);
        var genericDiagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public Shield<T> Build<T>() => Shield.For<T>().Retry(1).Fallback((T)default!);
            }
            """);

        await AssertRuleAsync(aliasDiagnostics, "KEV003");
        await AssertRuleAsync(genericDiagnostics, "KEV003");
    }

    [Test]
    public async Task KEV003_Skips_Valid_Or_Unknown_Compositions()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().Fallback(0).Retry(1);",
            "_ = Shield.For<int>().Retry(1).When<InvalidOperationException>().Fallback(0);",
            "_ = Shield.For<int>().Timeout(TimeSpan.FromSeconds(1)).Fallback(0);",
            "var shield = CreateShield(); _ = shield.Fallback(0);",
            "var shield = Shield.For<int>().Retry(1); shield = Shield<int>.Empty; _ = shield.Fallback(0);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1))).Fallback(0);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, "private static Shield<int> CreateShield() => Shield.For<int>().Retry(1);");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task Non_Kevlar_Methods_And_Generated_Code_Are_Ignored()
    {
        var unrelated = await AnalyzeSourceAsync("""
            public sealed class OtherShield
            {
                public OtherShield Hedge() => this;
                public OtherShield Retry() => this;
                public OtherShield Fallback() => this;
                public int Execute(Func<CancellationToken, int> action) => action(default);
            }

            public class TestSubject
            {
                public int Run() => new OtherShield().Hedge().Retry().Fallback().Execute(_ => 1);
            }
            """);
        var generated = await AnalyzeBodyAsync(
            "_ = Shield.Hedge(2, TimeSpan.Zero).Execute(_ => 1); _ = Shield.For<int>().Retry(1).Fallback(0);",
            isGenerated: true);

        await Assert.That(unrelated).IsEmpty();
        await Assert.That(generated).IsEmpty();
    }

    private static async Task AssertEachAsync(IEnumerable<string> cases, string expectedRule)
    {
        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await AssertRuleAsync(diagnostics, expectedRule);
        }
    }

    private static async Task AssertRuleAsync(ImmutableArray<Diagnostic> diagnostics, string expectedRule)
    {
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo(expectedRule);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeBodyAsync(
        string body,
        string members = "",
        bool isGenerated = false) =>
        AnalyzeSourceAsync($$"""
            public class TestSubject
            {
                {{members}}

                public void Run()
                {
                    {{body}}
                }
            }
            """, isGenerated);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(
        string declarations,
        bool isGenerated = false)
    {
        var source = (isGenerated ? "// <auto-generated/>\n" : string.Empty) + $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Kevlar;

            {{declarations}}
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "PipelineHazardAnalyzerTestSubject",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException("Test source does not compile: " + string.Join("; ", errors.Select(static error => error.ToString())));
        }

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineHazardAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}

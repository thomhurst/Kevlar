using System.Collections.Immutable;
using Kevlar.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kevlar.Analyzers.Tests;

/// <summary>
/// Guards KEV001: execution delegates that ignore the CancellationToken they are handed are
/// flagged; delegates that use it — and Execute-named methods outside Kevlar — are not.
/// </summary>
public class IgnoredCancellationTokenTests
{
    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string body) =>
        AnalyzeSourceAsync($$"""
            public class TestSubject
            {
                public async Task RunAsync()
                {
                    {{body}}
                }
            }
            """);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(string declarations)
    {
        var source = $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Kevlar;

            {{declarations}}
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "AnalyzerTestSubject",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (compileErrors.Count > 0)
        {
            throw new InvalidOperationException("Test source does not compile: " + string.Join("; ", compileErrors));
        }

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new IgnoredCancellationTokenAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }

    [Test]
    public async Task A_Lambda_That_Ignores_The_Token_Is_Flagged()
    {
        var diagnostics = await AnalyzeAsync("""
            var shield = Shield.Retry(1);
            await shield.ExecuteAsync(ct => new ValueTask<int>(1));
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("KEV001");
    }

    [Test]
    public async Task A_Lambda_That_Uses_The_Token_Is_Clean()
    {
        var diagnostics = await AnalyzeAsync("""
            var shield = Shield.Retry(1);
            await shield.ExecuteAsync(async ct => { await Task.Delay(1, ct); return 1; });
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task The_Task_Extension_Overloads_Are_Covered_Too()
    {
        var diagnostics = await AnalyzeAsync("""
            var shield = Shield.Retry(1);
            await shield.ExecuteAsync(ct => Task.FromResult(1));
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("KEV001");
    }

    [Test]
    public async Task State_Threading_Overloads_Are_Analyzed()
    {
        var flagged = await AnalyzeAsync("""
            var shield = Shield.Retry(1);
            await shield.ExecuteAsync(5, (state, ct) => new ValueTask<int>(state));
            """);
        await Assert.That(flagged.Length).IsEqualTo(1);

        var clean = await AnalyzeAsync("""
            var shield = Shield.Retry(1);
            await shield.ExecuteAsync(5, async (state, ct) => { await Task.Delay(state, ct); return state; });
            """);
        await Assert.That(clean.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Typed_Shields_Are_Analyzed()
    {
        var diagnostics = await AnalyzeAsync("""
            var shield = Shield.For<int>().WhenResult(0).Retry(1);
            await shield.ExecuteAsync(ct => new ValueTask<int>(1));
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Execute_Methods_Outside_Kevlar_Are_Ignored()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public static class OtherExecutor
            {
                public static Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action) => action(CancellationToken.None);
            }

            public class TestSubject
            {
                public async Task RunAsync() => await OtherExecutor.ExecuteAsync(ct => Task.FromResult(1));
            }
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }
}

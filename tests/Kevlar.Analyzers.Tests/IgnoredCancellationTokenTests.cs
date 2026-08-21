using System.Collections.Immutable;
using Kevlar.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kevlar.Analyzers.Tests;

/// <summary>
/// Guards KEV001: execution delegates that ignore the CancellationToken they are handed are
/// flagged; delegates that use it — and Execute-named methods outside Kevlar — are not.
/// </summary>
public class IgnoredCancellationTokenTests
{
    private static readonly (string Body, string ExpectedSignature)[] IgnoredExecutionOverloadCases =
    [
        ("var result = Shield.Empty.Execute(ct => 1);", "Kevlar.Shield.Execute(System.Func<System.Threading.CancellationToken,M0>,System.Threading.CancellationToken)"),
        ("var result = Shield.Empty.Execute(1, (state, ct) => state);", "Kevlar.Shield.Execute(M1,System.Func<M1,System.Threading.CancellationToken,M0>,System.Threading.CancellationToken)"),
        ("Shield.Empty.Execute(ct => { });", "Kevlar.Shield.Execute(System.Action<System.Threading.CancellationToken>,System.Threading.CancellationToken)"),
        ("Shield.Empty.Execute(1, (state, ct) => { _ = state; });", "Kevlar.Shield.Execute(M0,System.Action<M0,System.Threading.CancellationToken>,System.Threading.CancellationToken)"),
        ("var result = Shield.Empty.ExecuteWithContext(1, (_, _) => { }, (state, context) => state);", "Kevlar.Shield.ExecuteWithContext(M1,System.Action<M1,Kevlar.KevlarProperties>,System.Func<M1,Kevlar.KevlarContext,M0>,System.Threading.CancellationToken)"),
        ("Shield.Empty.ExecuteWithContext(1, (_, _) => { }, (state, context) => { _ = state; });", "Kevlar.Shield.ExecuteWithContext(M0,System.Action<M0,Kevlar.KevlarProperties>,System.Action<M0,Kevlar.KevlarContext>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteAsync(ct => new ValueTask<int>(1));", "Kevlar.Shield.ExecuteAsync(System.Func<System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteAsync(1, (state, ct) => new ValueTask<int>(state));", "Kevlar.Shield.ExecuteAsync(M1,System.Func<M1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteAsync(ct => ValueTask.CompletedTask);", "Kevlar.Shield.ExecuteAsync(System.Func<System.Threading.CancellationToken,System.Threading.Tasks.ValueTask>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteAsync(1, (state, ct) => { _ = state; return ValueTask.CompletedTask; });", "Kevlar.Shield.ExecuteAsync(M0,System.Func<M0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteWithContextAsync(1, (_, _) => { }, (state, context) => new ValueTask<int>(state));", "Kevlar.Shield.ExecuteWithContextAsync(M1,System.Action<M1,Kevlar.KevlarProperties>,System.Func<M1,Kevlar.KevlarContext,System.Threading.Tasks.ValueTask<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteWithContextAsync(1, (_, _) => { }, (state, context) => { _ = state; return ValueTask.CompletedTask; });", "Kevlar.Shield.ExecuteWithContextAsync(M0,System.Action<M0,Kevlar.KevlarProperties>,System.Func<M0,Kevlar.KevlarContext,System.Threading.Tasks.ValueTask>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteOutcomeAsync(ct => new ValueTask<int>(1));", "Kevlar.Shield.ExecuteOutcomeAsync(System.Func<System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteOutcomeAsync(1, (state, ct) => new ValueTask<int>(state));", "Kevlar.Shield.ExecuteOutcomeAsync(M1,System.Func<M1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteAsync(ct => Task.FromResult(1));", "Kevlar.ShieldTaskExtensions.ExecuteAsync(Kevlar.Shield,System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteAsync(1, (state, ct) => Task.FromResult(state));", "Kevlar.ShieldTaskExtensions.ExecuteAsync(Kevlar.Shield,M1,System.Func<M1,System.Threading.CancellationToken,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteAsync(ct => Task.CompletedTask);", "Kevlar.ShieldTaskExtensions.ExecuteAsync(Kevlar.Shield,System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteAsync(1, (state, ct) => { _ = state; return Task.CompletedTask; });", "Kevlar.ShieldTaskExtensions.ExecuteAsync(Kevlar.Shield,M0,System.Func<M0,System.Threading.CancellationToken,System.Threading.Tasks.Task>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteWithContextAsync(1, (_, _) => { }, (state, context) => Task.FromResult(state));", "Kevlar.ShieldTaskExtensions.ExecuteWithContextAsync(Kevlar.Shield,M1,System.Action<M1,Kevlar.KevlarProperties>,System.Func<M1,Kevlar.KevlarContext,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteWithContextAsync(1, (_, _) => { }, (state, context) => { _ = state; return Task.CompletedTask; });", "Kevlar.ShieldTaskExtensions.ExecuteWithContextAsync(Kevlar.Shield,M0,System.Action<M0,Kevlar.KevlarProperties>,System.Func<M0,Kevlar.KevlarContext,System.Threading.Tasks.Task>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteOutcomeAsync(ct => Task.FromResult(1));", "Kevlar.ShieldTaskExtensions.ExecuteOutcomeAsync(Kevlar.Shield,System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("await Shield.Empty.ExecuteOutcomeAsync(1, (state, ct) => Task.FromResult(state));", "Kevlar.ShieldTaskExtensions.ExecuteOutcomeAsync(Kevlar.Shield,M1,System.Func<M1,System.Threading.CancellationToken,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("var result = Shield<int>.Empty.Execute(ct => 1);", "Kevlar.Shield<T0>.Execute(System.Func<System.Threading.CancellationToken,T0>,System.Threading.CancellationToken)"),
        ("var result = Shield<int>.Empty.Execute(1, (state, ct) => state);", "Kevlar.Shield<T0>.Execute(M0,System.Func<M0,System.Threading.CancellationToken,T0>,System.Threading.CancellationToken)"),
        ("var result = Shield<int>.Empty.ExecuteWithContext(1, (_, _) => { }, (state, context) => state);", "Kevlar.Shield<T0>.ExecuteWithContext(M0,System.Action<M0,Kevlar.KevlarProperties>,System.Func<M0,Kevlar.KevlarContext,T0>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteAsync(ct => new ValueTask<int>(1));", "Kevlar.Shield<T0>.ExecuteAsync(System.Func<System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<T0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteAsync(1, (state, ct) => new ValueTask<int>(state));", "Kevlar.Shield<T0>.ExecuteAsync(M0,System.Func<M0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<T0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteWithContextAsync(1, (_, _) => { }, (state, context) => new ValueTask<int>(state));", "Kevlar.Shield<T0>.ExecuteWithContextAsync(M0,System.Action<M0,Kevlar.KevlarProperties>,System.Func<M0,Kevlar.KevlarContext,System.Threading.Tasks.ValueTask<T0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteOutcomeAsync(ct => new ValueTask<int>(1));", "Kevlar.Shield<T0>.ExecuteOutcomeAsync(System.Func<System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<T0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteOutcomeAsync(1, (state, ct) => new ValueTask<int>(state));", "Kevlar.Shield<T0>.ExecuteOutcomeAsync(M0,System.Func<M0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask<T0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteAsync(ct => Task.FromResult(1));", "Kevlar.ShieldTaskExtensions.ExecuteAsync(Kevlar.Shield<M0>,System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteAsync(1, (state, ct) => Task.FromResult(state));", "Kevlar.ShieldTaskExtensions.ExecuteAsync(Kevlar.Shield<M0>,M1,System.Func<M1,System.Threading.CancellationToken,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteWithContextAsync(1, (_, _) => { }, (state, context) => Task.FromResult(state));", "Kevlar.ShieldTaskExtensions.ExecuteWithContextAsync(Kevlar.Shield<M0>,M1,System.Action<M1,Kevlar.KevlarProperties>,System.Func<M1,Kevlar.KevlarContext,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteOutcomeAsync(ct => Task.FromResult(1));", "Kevlar.ShieldTaskExtensions.ExecuteOutcomeAsync(Kevlar.Shield<M0>,System.Func<System.Threading.CancellationToken,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
        ("await Shield<int>.Empty.ExecuteOutcomeAsync(1, (state, ct) => Task.FromResult(state));", "Kevlar.ShieldTaskExtensions.ExecuteOutcomeAsync(Kevlar.Shield<M0>,M1,System.Func<M1,System.Threading.CancellationToken,System.Threading.Tasks.Task<M0>>,System.Threading.CancellationToken)"),
    ];

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

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(
        string declarations,
        bool allowCompilationErrors = false,
        bool isGenerated = false)
    {
        var source = CreateSource(declarations, isGenerated);
        var compilation = CreateCompilation(source);

        var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (!allowCompilationErrors && compileErrors.Count > 0)
        {
            throw new InvalidOperationException("Test source does not compile: " + string.Join("; ", compileErrors));
        }

        return await GetAnalyzerDiagnosticsAsync(compilation);
    }

    private static string CreateSource(string declarations, bool isGenerated = false) =>
        (isGenerated ? "// <auto-generated/>\n" : string.Empty) + $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Kevlar;

            {{declarations}}
            """;

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location))
            .ToList();

        return CSharpCompilation.Create(
            "AnalyzerTestSubject",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(CSharpCompilation compilation) =>
        compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new IgnoredCancellationTokenAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

    private static string GetExecutionOverloadSignature(string body)
    {
        var compilation = CreateCompilation(CreateSource($$"""
            public class TestSubject
            {
                public async Task RunAsync()
                {
                    {{body}}
                }
            }
            """));
        var syntaxTree = compilation.SyntaxTrees.Single();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var method = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => semanticModel.GetSymbolInfo(invocation).Symbol)
            .OfType<IMethodSymbol>()
            .Single(IsExecutionMethod);

        return GetMethodSignature(method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition);
    }

    private static HashSet<string> GetPublicExecutionOverloadSignatures()
    {
        var compilation = CreateCompilation(CreateSource("public class TestSubject { }"));
        return new[] { "Kevlar.Shield", "Kevlar.Shield`1", "Kevlar.ShieldTaskExtensions" }
            .Select(compilation.GetTypeByMetadataName)
            .OfType<INamedTypeSymbol>()
            .SelectMany(type => type.GetMembers().OfType<IMethodSymbol>())
            .Where(method => method.DeclaredAccessibility == Accessibility.Public && IsExecutionMethod(method))
            .Select(method => GetMethodSignature(method.OriginalDefinition))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsExecutionMethod(IMethodSymbol method) =>
        method.Name is "Execute" or "ExecuteAsync" or "ExecuteOutcomeAsync"
            or "ExecuteWithContext" or "ExecuteWithContextAsync";

    private static string GetMethodSignature(IMethodSymbol method) =>
        $"{GetTypeSignature(method.ContainingType)}.{method.Name}({string.Join(",", method.Parameters.Select(parameter => GetTypeSignature(parameter.Type)))})";

    private static string GetTypeSignature(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol typeParameter)
        {
            var prefix = typeParameter.TypeParameterKind == TypeParameterKind.Method ? "M" : "T";
            return prefix + typeParameter.Ordinal;
        }

        var namedType = (INamedTypeSymbol)type;
        var name = namedType.ContainingNamespace.IsGlobalNamespace
            ? namedType.Name
            : $"{namedType.ContainingNamespace.ToDisplayString()}.{namedType.Name}";
        return namedType.TypeArguments.Length == 0
            ? name
            : $"{name}<{string.Join(",", namedType.TypeArguments.Select(GetTypeSignature))}>";
    }

    private static async Task AssertDiagnosticCountAsync(
        IEnumerable<string> cases,
        int expectedCount,
        string caseKind)
    {
        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeAsync(body);
            if (diagnostics.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedCount} KEV001 diagnostic(s) for {caseKind} case '{body}', but found {diagnostics.Length}.");
            }
        }
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
    public async Task An_Underscore_Token_Explicitly_Suppresses_The_Diagnostic()
    {
        var diagnostics = await AnalyzeAsync("""
            var shield = Shield.Retry(1);
            await shield.ExecuteAsync(_ => new ValueTask<int>(1));
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
    public async Task Context_Actions_Must_Use_The_Effective_Token()
    {
        var ignored = await AnalyzeAsync("""
            await Shield.Empty.ExecuteWithContextAsync(
                5,
                static (_, _) => { },
                static (state, context) => new ValueTask<int>(state + context.Properties.GetOrDefault(new KevlarKey<int>("value"))));
            """);
        await Assert.That(ignored.Length).IsEqualTo(1);

        var clean = await AnalyzeAsync("""
            await Shield.Empty.ExecuteWithContextAsync(
                5,
                static (_, _) => { },
                static (state, context) => new ValueTask<int>(state + context.CancellationToken.GetHashCode()));
            """);
        await Assert.That(clean.Length).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationToken_State_Does_Not_Suppress_The_Execution_Token()
    {
        var diagnostics = await AnalyzeAsync("""
            await Shield.Empty.ExecuteAsync(
                CancellationToken.None,
                (_, ct) => new ValueTask<int>(1));
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationToken_State_Does_Not_Hide_The_Context_Token()
    {
        var ignored = await AnalyzeAsync("""
            await Shield.Empty.ExecuteWithContextAsync(
                CancellationToken.None,
                static (state, properties) => properties.Set(new KevlarKey<int>("state"), state.GetHashCode()),
                static (state, context) => new ValueTask<int>(state.GetHashCode()));
            """);
        await Assert.That(ignored.Length).IsEqualTo(1);

        var clean = await AnalyzeAsync("""
            await Shield.Empty.ExecuteWithContextAsync(
                CancellationToken.None,
                static (state, properties) => properties.Set(new KevlarKey<int>("value"), 1),
                static (_, context) => new ValueTask<int>(context.CancellationToken.GetHashCode()));
            """);
        await Assert.That(clean.Length).IsEqualTo(0);
    }

    [Test]
    public async Task KevlarContext_State_Does_Not_Hide_The_Execution_Token()
    {
        var ignored = await AnalyzeAsync("""
            KevlarContext state = null!;
            await Shield.Empty.ExecuteAsync(
                state,
                static (contextState, ct) => new ValueTask<int>(contextState.CancellationToken.GetHashCode()));
            """);
        await Assert.That(ignored.Length).IsEqualTo(1);

        var clean = await AnalyzeAsync("""
            KevlarContext state = null!;
            await Shield.Empty.ExecuteAsync(
                state,
                static (contextState, ct) => new ValueTask<int>(ct.GetHashCode()));
            """);
        await Assert.That(clean.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Forwarded_Context_Is_Clean()
    {
        var diagnostics = await AnalyzeAsync("""
            static ValueTask<int> RunAsync(int state, KevlarContext context) =>
                new(state + context.CancellationToken.GetHashCode());

            await Shield.Empty.ExecuteWithContextAsync(
                5,
                static (_, _) => { },
                static (state, context) => RunAsync(state, context));
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Context_Token_Use_Through_A_Local_Alias_Is_Clean()
    {
        var diagnostics = await AnalyzeAsync("""
            await Shield.Empty.ExecuteWithContextAsync(
                5,
                static (_, _) => { },
                static (state, context) =>
                {
                    var activeContext = context;
                    return new ValueTask<int>(state + activeContext.CancellationToken.GetHashCode());
                });
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Context_Token_Use_Through_An_Assigned_Local_Alias_Is_Clean()
    {
        var diagnostics = await AnalyzeAsync("""
            await Shield.Empty.ExecuteWithContextAsync(
                5,
                static (_, _) => { },
                static (state, context) =>
                {
                    KevlarContext activeContext;
                    activeContext = context;
                    return new ValueTask<int>(state + activeContext.CancellationToken.GetHashCode());
                });
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
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

    [Test]
    public async Task Every_Public_Execution_Overload_Is_Analyzed()
    {
        foreach (var (body, expectedSignature) in IgnoredExecutionOverloadCases)
        {
            await Assert.That(GetExecutionOverloadSignature(body)).IsEqualTo(expectedSignature);
        }

        var expectedSignatures = IgnoredExecutionOverloadCases
            .Select(testCase => testCase.ExpectedSignature)
            .ToHashSet(StringComparer.Ordinal);
        await Assert.That(expectedSignatures.SetEquals(GetPublicExecutionOverloadSignatures())).IsTrue();
        await Assert.That(expectedSignatures.Count).IsEqualTo(IgnoredExecutionOverloadCases.Length);
        await AssertDiagnosticCountAsync(
            IgnoredExecutionOverloadCases.Select(testCase => testCase.Body),
            1,
            "public execution overload");
    }

    [Test]
    public async Task Lambda_Syntax_Forms_Are_Analyzed()
    {
        var cases = new[]
        {
            "var result = Shield.Empty.Execute(ct => 1);",
            "var result = Shield.Empty.Execute(ct => { return 1; });",
            "var result = Shield.Empty.Execute((CancellationToken ct) => 1);",
            "await Shield.Empty.ExecuteAsync(async ct => { await Task.Yield(); return 1; });",
            "Shield.Empty.Execute(delegate(CancellationToken ct) { });",
        };

        await AssertDiagnosticCountAsync(cases, 1, "lambda syntax");
    }

    [Test]
    public async Task Semantic_Token_Uses_Are_Clean()
    {
        var cases = new[]
        {
            "var result = Shield.Empty.Execute(ct => ct.CanBeCanceled ? 1 : 0);",
            "Shield.Empty.Execute(ct => { _ = ct; });",
            "await Shield.Empty.ExecuteAsync(ct => new ValueTask<int>(ct.GetHashCode()));",
            "await Shield.Empty.ExecuteAsync(async ct => { await Task.Delay(1, ct); });",
            "await Shield.Empty.ExecuteAsync(1, (state, ct) => Task.Delay(state, ct));",
            "await Shield.Empty.ExecuteOutcomeAsync(ct => Task.FromResult(ct.GetHashCode()));",
            "var result = Shield<int>.Empty.Execute(ct => ct.GetHashCode());",
            "await Shield<int>.Empty.ExecuteAsync(1, (state, ct) => Task.FromResult(state + ct.GetHashCode()));",
        };

        await AssertDiagnosticCountAsync(cases, 0, "token use");
    }

    [Test]
    public async Task Token_Use_Through_A_Local_Function_Is_Clean()
    {
        var diagnostics = await AnalyzeAsync("""
            await Shield.Empty.ExecuteAsync(ct =>
            {
                int ReadToken() => ct.GetHashCode();
                return new ValueTask<int>(ReadToken());
            });
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task A_Nested_Lambda_Does_Not_Mistake_Its_Own_Token_For_The_Execution_Token()
    {
        var diagnostics = await AnalyzeAsync("""
            await Shield.Empty.ExecuteAsync(ct => new ValueTask<int>(
                new Func<CancellationToken, int>(innerCt => innerCt.GetHashCode())(default)));
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Execution_Token_Captured_By_A_Nested_Lambda_Is_Clean()
    {
        var diagnostics = await AnalyzeAsync("""
            await Shield.Empty.ExecuteAsync(ct => new ValueTask<int>(
                new Func<int>(() => ct.GetHashCode())()));
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Method_Groups_Are_Ignored()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public int Run() => Shield.Empty.Execute(Work);

                private static int Work(CancellationToken cancellationToken) => 1;
            }
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Generated_Code_Is_Ignored()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public int Run() => Shield.Empty.Execute(ct => 1);
            }
            """, isGenerated: true);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Diagnostic_Contract_And_Location_Are_Exact()
    {
        const string lambda = "ct => 1";
        const string declarations = """
            public class TestSubject
            {
                public int Run() => Shield.Empty.Execute(ct => 1);
            }
            """;
        var source = CreateSource(declarations);
        var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateCompilation(source));

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV001");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "The delegate passed to 'Execute' never uses the CancellationToken it is handed; timeouts and cancellation cannot stop it. Pass the token to the work inside, or name it '_' only if the work is truly uncancellable.");
        await Assert.That(diagnostic.Location.SourceSpan.Start).IsEqualTo(source.IndexOf(lambda, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(lambda.Length);
    }

    [Test]
    public async Task Malformed_Compilation_Does_Not_Crash_The_Analyzer()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public async Task RunAsync()
                {
                    await Shield.Empty.ExecuteAsync(ct =>
                }
            }
            """, allowCompilationErrors: true);

        await Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "AD0001")).IsFalse();
    }

    [Test]
    public async Task Concurrent_Analyzer_Runs_Are_Deterministic()
    {
        var source = CreateSource("""
            public class TestSubject
            {
                public async Task RunAsync()
                {
                    Shield.Empty.Execute(ct => { });
                    await Shield.Empty.ExecuteAsync(ct => Task.CompletedTask);
                    await Shield<int>.Empty.ExecuteOutcomeAsync(ct => new ValueTask<int>(1));
                }
            }
            """);
        var compilation = CreateCompilation(source);
        var runs = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => GetAnalyzerDiagnosticsAsync(compilation)));
        var expected = runs[0]
            .Select(diagnostic => diagnostic.Location.SourceSpan)
            .OrderBy(span => span.Start)
            .ToArray();

        await Assert.That(expected.Length).IsEqualTo(3);
        foreach (var run in runs)
        {
            var actual = run
                .Select(diagnostic => diagnostic.Location.SourceSpan)
                .OrderBy(span => span.Start)
                .ToArray();
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException("Concurrent analyzer runs produced different diagnostic spans.");
            }
        }
    }
}

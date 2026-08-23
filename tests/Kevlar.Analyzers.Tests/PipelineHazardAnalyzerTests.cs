using System.Collections.Immutable;
using Kevlar.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kevlar.Analyzers.Tests;

public class PipelineHazardAnalyzerTests
{
    [Test]
    public async Task Legacy_Fallback_Callbacks_Cannot_Bind_As_Options_Configurators()
    {
        var ambiguous = CreateCompilation(CreateSource("""
            public class TestSubject
            {
                public void Configure()
                {
                    _ = Shield.For<int>().Fallback(42, _ => { });
                    _ = Shield.For<int>().Fallback(_ => new ValueTask<int>(42), _ => { });
                    _ = Shield.For<int>().Fallback((_, _) => new ValueTask<int>(42), _ => { });
                    _ = Shield.For<int>().When<Exception>().Fallback(42, _ => { });
                    _ = Shield.For<int>().When<Exception>().Fallback(_ => new ValueTask<int>(42), _ => { });
                    _ = Shield.For<int>().When<Exception>().Fallback((_, _) => new ValueTask<int>(42), _ => { });
                }
            }
            """));
        var ambiguousErrors = ambiguous.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(ambiguousErrors).Count().IsEqualTo(6);
        await Assert.That(ambiguousErrors).All(static diagnostic => diagnostic.Id == "CS0121");

        var explicitLegacy = CreateCompilation(CreateSource("""
            public class TestSubject
            {
                public void Configure()
                {
                    Action<FallbackEvent<int>> callback = _ => { };
                    _ = Shield.For<int>().Fallback(42, callback);
                }
            }
            """));
        var explicitErrors = explicitLegacy.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(explicitErrors).Count().IsEqualTo(1);
        await Assert.That(explicitErrors[0].Id).IsEqualTo("CS0619");
    }

    [Test]
    public async Task KEV004_Flags_Inline_Stateful_Shields_For_All_Execution_Forms()
    {
        var cases = new[]
        {
            "_ = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            "await Shield.RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1));",
            "await Shield.ConcurrencyLimit(2).ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "_ = Shield.For<int>().CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            "await Shield.For<int>().RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1));",
            "await Shield.For<int>().ConcurrencyLimit(2).ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "await Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => Task.FromResult(1));",
            "await Shield.For<int>().RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteOutcomeAsync(_ => Task.FromResult(1));",
            "Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => { });",
            "_ = Shield.RateLimit(10, TimeSpan.FromSeconds(1)).Execute(1, (state, _) => state);",
            "await Shield.ConcurrencyLimit(2).ExecuteAsync(_ => ValueTask.CompletedTask);",
            "await Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(1, (state, _) => Task.FromResult(state));",
            "await Shield.RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteOutcomeAsync(1, (state, _) => new ValueTask<int>(state));",
            "_ = Shield.For<int>().CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(1, (state, _) => state);",
            "await Shield.For<int>().RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteAsync(1, (state, _) => Task.FromResult(state));",
            "await Shield.For<int>().ConcurrencyLimit(2).ExecuteOutcomeAsync(1, (state, _) => new ValueTask<int>(state));",
        };

        await AssertEachAsync(cases, "KEV004");
    }

    [Test]
    public async Task KEV004_Flags_Single_Use_Locals_Aliases_And_Extension_Syntax()
    {
        var cases = new[]
        {
            "var shield = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)); _ = shield.Execute(_ => 1);",
            "var shield = Shield.RateLimit(10, TimeSpan.FromSeconds(1)); var alias = shield; await alias.ExecuteAsync(_ => new ValueTask<int>(1));",
            "_ = ShieldExtensions.ConcurrencyLimit(Shield.Empty, 2).Execute(_ => 1);",
            "Func<ValueTask<int>> run = () => Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1)); await run();",
        };

        await AssertEachAsync(cases, "KEV004");

        var typeAlias = await AnalyzeSourceAsync("""
            using KShield = Kevlar.Shield;

            public class TestSubject
            {
                public int Run() => KShield.RateLimit(10, TimeSpan.FromSeconds(1)).Execute(_ => 1);
            }
            """);
        await AssertRuleAsync(typeAlias, "KEV004");
    }

    [Test]
    public async Task KEV004_Flags_Stateful_Composition_Operands()
    {
        var cases = new[]
        {
            "_ = Shield.Empty.Wrap(Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1))).Execute(_ => 1);",
            "_ = Shield.Compose(Shield.Empty, Shield.RateLimit(10, TimeSpan.FromSeconds(1))).Execute(_ => 1);",
            "_ = Shield.Compose([Shield.ConcurrencyLimit(2)]).Execute(_ => 1);",
            "_ = Shield.Compose([.. new[] { Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)) }]).Execute(_ => 1);",
            "var parts = new[] { Shield.RateLimit(10, TimeSpan.FromSeconds(1)) }; _ = Shield.Compose(parts).Execute(_ => 1);",
        };

        await AssertEachAsync(cases, "KEV004");
    }

    [Test]
    public async Task KEV004_Flags_Per_Execution_Partition_Providers()
    {
        var cases = new[]
        {
            "_ = new PartitionedShield<string>(_ => Shield.Empty).GetShield(\"tenant\").Execute(_ => 1);",
            "await new PartitionedShield<string, int>(_ => Shield<int>.Empty).GetShield(\"tenant\").ExecuteAsync(_ => new ValueTask<int>(1));",
            "var partitions = new PartitionedShield<string>(_ => Shield.Empty); await partitions.GetShield(\"tenant\").ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "var partitions = new PartitionedShield<string>(_ => Shield.Empty); var shield = partitions.GetShield(\"tenant\"); _ = shield.Execute(_ => 1);",
        };

        await AssertEachAsync(cases, "KEV004");
    }

    [Test]
    public async Task KEV004_Skips_Stateless_Reused_And_Ambiguous_Shields()
    {
        var cases = new[]
        {
            "_ = Shield.Retry(2).Execute(_ => 1);",
            "await Shield.Timeout(TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1));",
            "await Shield.For<int>().Fallback(0).ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "var shield = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)); _ = shield.Execute(_ => 1); _ = shield.Execute(_ => 2);",
            "var shield = CreateShield(); _ = shield.Execute(_ => 1);",
            "var partitions = new PartitionedShield<string>(_ => Shield.Empty); _ = partitions.GetShield(\"one\"); _ = partitions.GetShield(\"two\").Execute(_ => 1);",
            "var shield = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)); Func<int> run = () => shield.Execute(_ => 1); _ = run();",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                "private static Shield CreateShield() => Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1));");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV004_Skips_Fields_Parameters_Factories_Registrations_And_Test_Assemblies()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private static readonly Shield Shared = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1));
                private readonly PartitionedShield<string> _partitions = new(_ => Shield.Empty);

                public int FromField() => Shared.Execute(_ => 1);
                public int FromParameter(Shield shield) => shield.Execute(_ => 1);
                public int FromPartitionField() => _partitions.GetShield("tenant").Execute(_ => 1);
                public Shield Create() => Shield.RateLimit(10, TimeSpan.FromSeconds(1));
                public void Configure() => Register(Shield.ConcurrencyLimit(2));
                private static void Register(Shield shield) { }
            }
            """);
        var testAssemblyDiagnostics = await AnalyzeBodyAsync(
            "_ = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            assemblyName: "Sample.Tests");
        var testMethodDiagnostics = await AnalyzeSourceAsync("""
            namespace Xunit
            {
                public sealed class FactAttribute : Attribute { }
            }

            public sealed class TestSubject
            {
                [Xunit.Fact]
                public int IsolatedExecution() =>
                    Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
        await Assert.That(testAssemblyDiagnostics).IsEmpty();
        await Assert.That(testMethodDiagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV004_Ignores_Lookalikes_Generated_Code_And_Malformed_Code()
    {
        var lookalike = await AnalyzeSourceAsync("""
            public sealed class OtherShield
            {
                public static OtherShield CircuitBreaker() => new();
                public int Execute(Func<CancellationToken, int> action) => action(default);
            }

            public class TestSubject
            {
                public int Run() => OtherShield.CircuitBreaker().Execute(_ => 1);
            }
            """);
        var generated = await AnalyzeBodyAsync(
            "_ = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);",
            isGenerated: true);
        var malformed = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public async Task RunAsync()
                {
                    await Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).ExecuteAsync(_ =>
                }
            }
            """, allowCompilationErrors: true);
        var malformedAttribute = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                [MissingTest]
                public int Run() =>
                    Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);
            }
            """, allowCompilationErrors: true);

        await Assert.That(lookalike).IsEmpty();
        await Assert.That(generated).IsEmpty();
        await Assert.That(malformed.Any(diagnostic => diagnostic.Id == "AD0001")).IsFalse();
        await Assert.That(malformedAttribute.Any(diagnostic => diagnostic.Id == "AD0001")).IsFalse();
    }

    [Test]
    public async Task KEV004_Diagnostic_Contract_Location_And_Suppression_Are_Exact()
    {
        const string construction = "Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1))";
        var source = $$"""
            public class TestSubject
            {
                public int Run() => {{construction}}.Execute(_ => 1);
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public int Run()
                {
            #pragma warning disable KEV004 // Isolated execution is intentional.
                    return Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);
            #pragma warning restore KEV004
                }
            }
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV004");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "'CircuitBreaker' creates resilience state for one execution. Store and reuse the shield or partition provider as a field, singleton/keyed DI registration, or registry entry.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(construction, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(construction.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV004_Concurrent_Analyzer_Runs_Are_Deterministic()
    {
        var source = CreateSource("""
            public class TestSubject
            {
                public async Task RunAsync()
                {
                    _ = Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1)).Execute(_ => 1);
                    await Shield.RateLimit(10, TimeSpan.FromSeconds(1)).ExecuteAsync(_ => new ValueTask<int>(1));
                    await new PartitionedShield<string>(_ => Shield.Empty).GetShield("tenant").ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
                }
            }
            """);
        var compilation = CreateCompilation(source);
        var runs = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => GetAnalyzerDiagnosticsAsync(compilation)));
        var expected = runs[0]
            .Where(diagnostic => diagnostic.Id == "KEV004")
            .Select(diagnostic => diagnostic.Location.SourceSpan)
            .OrderBy(span => span.Start)
            .ToArray();

        await Assert.That(expected.Length).IsEqualTo(3);
        foreach (var run in runs)
        {
            var actual = run
                .Where(diagnostic => diagnostic.Id == "KEV004")
                .Select(diagnostic => diagnostic.Location.SourceSpan)
                .OrderBy(span => span.Start)
                .ToArray();
            await Assert.That(actual).IsEquivalentTo(expected);
        }
    }

    [Test]
    public async Task KEV005_Flags_Inline_Void_Fallback_For_Each_Result_Execution_Method()
    {
        var cases = new[]
        {
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => 1);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => new ValueTask<int>(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => new ValueTask<int>(1));",
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContext(0, static (_, _) => { }, static (_, _) => 1);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContextAsync(0, static (_, _) => { }, static (_, _) => new ValueTask<int>(1));",
        };

        await AssertEachAsync(cases, "KEV005");
    }

    [Test]
    public async Task KEV005_Flags_Task_Extensions_And_Transitional_Fallback_Overload()
    {
        var cases = new[]
        {
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => Task.FromResult(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => Task.FromResult(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContextAsync(0, static (_, _) => { }, static (_, _) => Task.FromResult(1));",
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask, static options => options.OnFallback = static _ => { }).Execute(static _ => 1);",
        };

        await AssertEachAsync(cases, "KEV005");
    }

    [Test]
    public async Task KEV005_Flags_Stable_Locals_Aliases_Builders_And_Result_Lifts()
    {
        var cases = new[]
        {
            "var shield = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = shield.Execute(static _ => 1);",
            "var shield = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); var alias = shield; _ = alias.Execute(static _ => 1);",
            "var shield = Shield.When<InvalidOperationException>().Fallback(static (_, _) => ValueTask.CompletedTask); _ = shield.Execute(static _ => 1);",
            "var shield = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = shield.For<int>().Execute(static _ => 1);",
            "_ = Shield.Empty.Wrap(Shield.Empty.Fallback(static _ => ValueTask.CompletedTask)).Execute(static _ => 1);",
            "var fallback = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = Shield.Compose(Shield.Empty, fallback).Execute(static _ => 1);",
        };

        await AssertEachAsync(cases, "KEV005");
    }

    [Test]
    public async Task KEV005_Skips_Typed_Fallbacks_And_Void_Executions()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().Fallback(0).Execute(static _ => 1);",
            "Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => { });",
            "await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => ValueTask.CompletedTask);",
            "Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContext(0, static (_, _) => { }, static (_, _) => { });",
            "await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContextAsync(0, static (_, _) => { }, static (_, _) => ValueTask.CompletedTask);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV005_Defers_Fields_And_Skips_Escaped_Or_Unstable_Locals()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class TestSubject
            {
                private static readonly Shield Shared = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask);

                public int FromField() => Shared.Execute(static _ => 1);

                public Shield Escape() => Shield.Empty.Fallback(static _ => ValueTask.CompletedTask);

                public int FromParameter(Shield shield) => shield.Execute(static _ => 1);

                public int Reassigned()
                {
                    var shield = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask);
                    shield = Shield.Empty;
                    return shield.Execute(static _ => 1);
                }
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV005_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => 1);");
        var suppressed = await AnalyzeBodyAsync("""
            #pragma warning disable KEV005 // Result use is validated elsewhere.
            _ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => 1);
            #pragma warning restore KEV005
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV005");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "Fallback on a non-generic Shield applies only to void executions. " +
            "For executions that return a value, build a result-aware shield with " +
            "Shield.For<T>() and use its Fallback overloads.");
        await Assert.That(suppressed).IsEmpty();
    }

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
            "_ = Shield.Compose([Shield.Hedge(2, TimeSpan.Zero)]).Execute(_ => 1);",
            "_ = Shield.Compose([.. new[] { Shield.Hedge(2, TimeSpan.Zero) }]).Execute(_ => 1);",
            "var parts = new[] { Shield.Hedge(2, TimeSpan.Zero) }; _ = Shield.Compose(parts).Execute(_ => 1);",
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
    public async Task KEV002_Skips_Aliased_And_Mutated_Shield_Arrays()
    {
        var cases = new[]
        {
            "var parts = new[] { Shield.Hedge(2, TimeSpan.Zero) }; parts[0] = Shield.Empty; _ = Shield.Compose(parts).Execute(_ => 1);",
            "var parts = new[] { Shield.Hedge(2, TimeSpan.Zero) }; var alias = parts; alias[0] = Shield.Empty; _ = Shield.Compose(parts).Execute(_ => 1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
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
            "_ = Shield.For<int>().Retry(1).Fallback(0, static options => options.OnFallback = static _ => { });",
            "_ = Shield.Retry(1).Fallback(static _ => ValueTask.CompletedTask, static options => options.OnFallback = static _ => { });",
            "_ = Shield.For<int>().Retry(1).WhenAnyError().Fallback(0);",
            "_ = Shield.For<int>().Retry(1).When<InvalidOperationException>().Timeout(TimeSpan.Zero).WhenAnyError().Fallback(0);",
            "var shield = Shield.For<int>().Retry(1); var alias = shield; _ = alias.Fallback(0);",
            "Shield<int>? shield = Shield.For<int>().Retry(1); _ = shield?.Fallback(0);",
            "_ = ShieldExtensions.Fallback(ShieldExtensions.Retry(Shield.Empty, 1), static _ => ValueTask.CompletedTask);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.Empty).Fallback(0);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.Timeout(TimeSpan.FromSeconds(1))).Fallback(0);",
            "_ = Shield<int>.Empty.Wrap(Shield.Retry(1)).Fallback(0);",
            "_ = Shield.Compose(Shield.Retry(1)).For<int>().Fallback(0);",
            "_ = Shield.Compose(Shield.Timeout(TimeSpan.FromSeconds(1)), Shield.Retry(1)).For<int>().Fallback(0);",
            "_ = Shield.Compose([Shield.Retry(1)]).For<int>().Fallback(0);",
            "var parts = new[] { Shield.Retry(1) }; _ = Shield.Compose(parts).For<int>().Fallback(0);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1))).Fallback(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Wrap(Shield.Retry(1)).Fallback(0);",
            "_ = Shield.Compose(Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)), Shield.Retry(1)).For<int>().Fallback(0);",
            "_ = Shield.For<int>().Retry(1).When<ArgumentException>().Timeout(TimeSpan.Zero).Wrap(Shield.Empty).Fallback(0);",
            "_ = Shield.Compose(Shield.Retry(1).When<ArgumentException>().Timeout(TimeSpan.Zero), Shield.Empty).For<int>().Fallback(0);",
            "var retry = Shield.Retry(1); var fallback = Shield.For<int>().Fallback(0); _ = retry.Wrap(fallback);",
            "var retry = Shield.Retry(1); var fallback = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = Shield.Compose(retry, fallback);",
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
            "_ = Shield.For<int>().When<InvalidOperationException>().Retry(1).WhenAnyError().Fallback(0);",
            "_ = Shield.For<int>().Timeout(TimeSpan.FromSeconds(1)).Fallback(0);",
            "var shield = CreateShield(); _ = shield.Fallback(0);",
            "var shield = Shield.For<int>().Retry(1); shield = Shield<int>.Empty; _ = shield.Fallback(0);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1).Wrap(Shield.Empty).Fallback(0);",
            "_ = Shield.Compose(Shield.When<InvalidOperationException>().Retry(1), Shield.Empty).For<int>().Fallback(0);",
            "var clause = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.Zero); var outer = clause.Retry(1); _ = outer.Wrap(clause).Fallback(0);",
            "var clause = Shield.When<InvalidOperationException>().Timeout(TimeSpan.Zero); var outer = clause.Retry(1); _ = Shield.Compose(outer, clause).For<int>().Fallback(0);",
            "var parts = new[] { Shield.Retry(1) }; parts[0] = Shield.Empty; _ = Shield.Compose(parts).For<int>().Fallback(0);",
            "var builder = Shield.For<int>().When<InvalidOperationException>(); var retry = builder.Retry(1); _ = retry.Wrap(builder.Timeout(TimeSpan.Zero)).Fallback(0);",
            "var fallback = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); var retry = Shield.Retry(1); _ = Shield.Compose(fallback, retry);",
            "var retry = Shield.When<InvalidOperationException>().Retry(1); var fallback = Shield.For<int>().When<TimeoutException>().Fallback(0); _ = retry.Wrap(fallback);",
            "var outer = Shield.Retry(1).When<InvalidOperationException>().Timeout(TimeSpan.Zero); var fallback = Shield.For<int>().When<InvalidOperationException>().Fallback(0); _ = outer.Wrap(fallback);",
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
        bool isGenerated = false,
        string assemblyName = "PipelineHazardAnalyzerTestSubject") =>
        AnalyzeSourceAsync($$"""
            public class TestSubject
            {
                {{members}}

                public async Task RunAsync()
                {
                    {{body}}
                }
            }
            """, isGenerated, assemblyName: assemblyName);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(
        string declarations,
        bool isGenerated = false,
        bool allowCompilationErrors = false,
        string assemblyName = "PipelineHazardAnalyzerTestSubject")
    {
        var source = CreateSource(declarations, isGenerated);
        var compilation = CreateCompilation(source, assemblyName);
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (!allowCompilationErrors && errors.Length > 0)
        {
            throw new InvalidOperationException("Test source does not compile: " + string.Join("; ", errors.Select(static error => error.ToString())));
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

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "PipelineHazardAnalyzerTestSubject")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location));
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(CSharpCompilation compilation) =>
        compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PipelineHazardAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
}

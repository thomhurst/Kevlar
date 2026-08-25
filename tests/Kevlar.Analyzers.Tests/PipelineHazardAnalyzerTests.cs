using System.Collections.Immutable;
using Kevlar.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Reflection;

namespace Kevlar.Analyzers.Tests;

public class PipelineHazardAnalyzerTests
{
    [Test]
    public async Task Public_Surface_Uses_One_Hedge_Stem()
    {
        var legacyNames = typeof(PipelineHazardAnalyzer).Assembly.ExportedTypes
            .SelectMany(static type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(static member => member.Name)
                .Append(type.Name))
            .Where(static name => name.Contains("Hedging", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToArray();

        await Assert.That(legacyNames).IsEmpty();
    }

    [Test]
    public async Task Custom_Strategy_Can_Declare_Single_Invocation_Without_Diagnostics()
    {
        var diagnostics = await AnalyzeSourceAsync("""
            public sealed class SingleInvocationStrategy : Strategy
            {
                protected override bool InvokesContinuationAtMostOnce => true;

                public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
                    Continuation<T, TState> next,
                    KevlarContext context) => next.InvokeAsync(context);
            }
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task Typed_Fallback_Keeps_Only_The_Bare_And_Configure_Tiers()
    {
        var supported = CreateCompilation(CreateSource("""
            public class TestSubject
            {
                public void Configure()
                {
                    _ = Shield.For<int>().FallbackTo(42);
                    _ = Shield.For<int>().Fallback(_ => new ValueTask<int>(42));
                    _ = Shield.For<int>().Fallback((_, _) => new ValueTask<int>(42));
                    _ = Shield.For<int>().FallbackTo(42, options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().Fallback(_ => new ValueTask<int>(42), options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().Fallback((_, _) => new ValueTask<int>(42), options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().When<Exception>().FallbackTo(42);
                    _ = Shield.For<int>().When<Exception>().Fallback(_ => new ValueTask<int>(42));
                    _ = Shield.For<int>().When<Exception>().Fallback((_, _) => new ValueTask<int>(42));
                    _ = Shield.For<int>().When<Exception>().FallbackTo(42, options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().When<Exception>().Fallback(_ => new ValueTask<int>(42), options => options.OnFallback = _ => { });
                    _ = Shield.For<int>().When<Exception>().Fallback((_, _) => new ValueTask<int>(42), options => options.OnFallback = _ => { });
                }
            }
            """));
        var supportedErrors = supported.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(supportedErrors).IsEmpty();

        var legacyCallback = CreateCompilation(CreateSource("""
            public class TestSubject
            {
                public void Configure()
                {
                    Action<FallbackEvent<int>> callback = _ => { };
                    _ = Shield.For<int>().FallbackTo(42, callback);
                    _ = Shield.For<int>().When<Exception>().FallbackTo(42, callback);
                }
            }
            """));
        var legacyErrors = legacyCallback.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(legacyErrors).Count().IsEqualTo(2);
        await Assert.That(legacyErrors).All(static diagnostic => diagnostic.Id == "CS1503");
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
            "_ = Shield.Empty.UseRateLimiter((System.Threading.RateLimiting.RateLimiter)null!).Execute(_ => 1);",
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
            "new PartitionedShield<string>(_ => Shield.Fallback(static _ => ValueTask.CompletedTask)).GetShield(\"tenant\").Execute(static _ => { });",
            "await new PartitionedShield<string, int>(_ => Shield<int>.Empty).GetShield(\"tenant\").ExecuteAsync(_ => new ValueTask<int>(1));",
            "var partitions = new PartitionedShield<string>(_ => Shield.Empty); await partitions.GetShield(\"tenant\").ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
            "var partitions = new PartitionedShield<string>(_ => Shield.Empty); var shield = partitions.GetShield(\"tenant\"); _ = shield.Execute(_ => 1);",
            "await (await PartitionedShield<string>.CreateAsync(_ => new ValueTask<Shield>(Shield.Empty)).GetShieldAsync(\"tenant\")).ExecuteAsync(_ => new ValueTask<int>(1));",
            "var partitions = PartitionedShield<string, int>.CreateAsync(_ => new ValueTask<Shield<int>>(Shield<int>.Empty)); await (await partitions.GetShieldAsync(\"tenant\")).ExecuteAsync(_ => new ValueTask<int>(1));",
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
            "await Shield.For<int>().FallbackTo(0).ExecuteOutcomeAsync(_ => new ValueTask<int>(1));",
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
    public async Task Void_Fallback_Preserves_The_Shield_Type_And_Fluent_Surface()
    {
        var compilation = CreateCompilation(CreateSource("""
            public sealed class TestSubject
            {
                public async Task Run()
                {
                    Shield fromFactory = Shield.Fallback(static _ => ValueTask.CompletedTask);
                    Shield fromExtension = Shield.Retry(1).Fallback(static _ => ValueTask.CompletedTask);
                    Shield fromBuilder = Shield.When<InvalidOperationException>()
                        .Fallback(static (_, _) => ValueTask.CompletedTask);
                    Shield chained = fromExtension
                        .Retry()
                        .Timeout(TimeSpan.FromSeconds(1))
                        .CircuitBreaker(2, TimeSpan.FromSeconds(1))
                        .RateLimit(10, TimeSpan.FromSeconds(1))
                        .ConcurrencyLimit(2)
                        .Hedge(1, TimeSpan.Zero)
                        .When<TimeoutException>()
                        .Or<InvalidOperationException>()
                        .Retry(1, Backoff.None)
                        .WhenAnyError()
                        .WithName("void")
                        .WithTimeProvider(TimeProvider.System);
                    Shield outer = Shield.Timeout(TimeSpan.FromSeconds(1)).Wrap(chained);
                    Shield inner = chained.Wrap(Shield.Retry(1));

                    fromFactory.Execute(static _ => { });
                    fromBuilder.Execute(1, static (_, _) => { });
                    await outer.ExecuteAsync(static _ => ValueTask.CompletedTask);
                    await inner.ExecuteAsync(1, static (_, _) => ValueTask.CompletedTask);
                    await chained.ExecuteAsync(static _ => Task.CompletedTask);
                    await chained.ExecuteWithContextAsync(static _ => ValueTask.CompletedTask);
                    await chained.ExecuteWithContextAsync(1, static (_, _) => { }, static (_, _) => ValueTask.CompletedTask);
                }
            }
            """));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task KEV005_Flags_Void_Fallback_For_Each_Result_Execution_Method()
    {
        var cases = new[]
        {
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => 1);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => new ValueTask<int>(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => new ValueTask<int>(1));",
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContext(0, static (_, _) => { }, static (_, _) => 1);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteWithContextAsync(0, static (_, _) => { }, static (_, _) => new ValueTask<int>(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => Task.FromResult(1));",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => Task.FromResult(1));",
        };

        await AssertEachAsync(cases, "KEV005");
    }

    [Test]
    public async Task KEV005_Follows_Aliases_Builders_Result_Lifts_And_Composition()
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
    public async Task KEV005_Follows_The_Rate_Limiter_Adapter()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask)" +
            ".UseRateLimiter((System.Threading.RateLimiting.RateLimiter)null!).Execute(static _ => 1);");

        await AssertRuleAsync(Without(diagnostics, "KEV004"), "KEV005");
    }

    [Test]
    public async Task KEV005_Skips_Typed_Fallbacks_And_Void_Executions()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().FallbackTo(0).Execute(static _ => 1);",
            "Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).Execute(static _ => { });",
            "await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteAsync(static _ => ValueTask.CompletedTask);",
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcome(static _ => { });",
            "_ = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcome(0, static (_, _) => { });",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => ValueTask.CompletedTask);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(0, static (_, _) => ValueTask.CompletedTask);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(static _ => Task.CompletedTask);",
            "_ = await Shield.Empty.Fallback(static _ => ValueTask.CompletedTask).ExecuteOutcomeAsync(0, static (_, _) => Task.CompletedTask);",
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
            "_ = Shield.Hedge(2, TimeSpan.Zero).ExecuteOutcome(_ => 1);",
            "_ = Shield.For<int>().Hedge(2, TimeSpan.Zero).ExecuteOutcome(_ => 1);",
        };

        await AssertEachAsync(cases, "KEV002", "KEV006");
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

        await AssertRuleAsync(Without(aliasDiagnostics, "KEV006"), "KEV002");
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
            await Assert.That(Without(diagnostics, "KEV006")).IsEmpty();
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
            await Assert.That(Without(diagnostics, "KEV006")).IsEmpty();
        }
    }

    [Test]
    public async Task KEV006_Flags_Hedging_On_Untyped_Shields_And_Builders()
    {
        var cases = new[]
        {
            "_ = Shield.Hedge(2, TimeSpan.Zero);",
            "_ = Shield.Hedge(options => options.MaxAttempts = 2);",
            "_ = Shield.Empty.Hedge(2, TimeSpan.Zero);",
            "_ = Shield.Empty.Hedge(options => options.MaxAttempts = 2);",
            "_ = ShieldExtensions.Hedge(Shield.Empty, 2, TimeSpan.Zero);",
            "_ = Shield.When<InvalidOperationException>().Hedge(2, TimeSpan.Zero);",
            "_ = Shield.When<InvalidOperationException>().Hedge(options => options.MaxAttempts = 2);",
            "_ = Shield.Timeout(TimeSpan.FromSeconds(1)).Hedge(2, TimeSpan.Zero).Retry(1);",
        };

        await AssertEachAsync(cases, "KEV006");
    }

    [Test]
    public async Task KEV006_Skips_Typed_Shields_And_Typed_Builders()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().Hedge(2, TimeSpan.Zero);",
            "_ = Shield.For<int>().Hedge(options => options.MaxAttempts = 2);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Hedge(2, TimeSpan.Zero);",
            "_ = Shield.For<int>().WhenResult(0).Hedge(options => options.MaxAttempts = 2);",
            "_ = Shield.Empty.For<int>().Hedge(2, TimeSpan.Zero);",
            "_ = Shield.For<int>().Retry(1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV006_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string construction = "Shield.Hedge(2, TimeSpan.Zero)";
        var source = $$"""
            public class TestSubject
            {
                public Shield Build() => {{construction}};
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeBodyAsync("""
            #pragma warning disable KEV006 // The documented action is idempotent.
            _ = Shield.Hedge(2, TimeSpan.Zero);
            #pragma warning restore KEV006
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV006");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "Hedging on an untyped Shield runs the execution delegate more than once, concurrently. "
            + "Build a result-aware shield with Shield.For<T>() so result clauses can select the "
            + "winning attempt, or confirm the action is idempotent.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(construction, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(construction.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV007_Flags_Clause_Builders_That_Never_Reach_A_Strategy()
    {
        var cases = new[]
        {
            "Shield.When<InvalidOperationException>();",
            "Shield.When<InvalidOperationException>().Or<TimeoutException>();",
            "Shield.WhenContext((HandlingEvent handling) => handling.Attempt == 0);",
            "Shield.When<InvalidOperationException>().Or<TimeoutException>().Or(static exception => exception is null);",
            "_ = Shield.When<InvalidOperationException>();",
            "_ = Shield.Empty.When<InvalidOperationException>();",
            "_ = Shield.For<int>().WhenResult(static value => value < 0);",
            "_ = Shield.For<int>().WhenResultIsDefault().Or<TimeoutException>();",
            "var clause = Shield.When<InvalidOperationException>().Or<TimeoutException>();",
            "var clause = Shield.For<int>().When<InvalidOperationException>();",

            // Builders are immutable, so an Or… whose new builder is dropped extends nothing —
            // the stored builder still carries InvalidOperationException alone.
            "var clause = Shield.When<InvalidOperationException>(); clause.Or<TimeoutException>(); _ = clause.Retry(1);",
            "var clause = Shield.For<int>().When<InvalidOperationException>(); clause.OrResultIsDefault(); _ = clause.Retry(1);",
        };

        // The int cases also draw KEV010: a default-result clause on a value type is its own hint.
        await AssertEachAsync(cases, "KEV007", "KEV010");
    }

    [Test]
    public async Task KEV007_Flags_A_Clause_Replaced_Before_Any_Reactive_Strategy()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).WhenAnyError().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Timeout(static options => options.Timeout = TimeSpan.FromSeconds(1)).WhenAnyError().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).When<TimeoutException>().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Or<TimeoutException>().RateLimit(1, TimeSpan.FromSeconds(1)).When<TimeoutException>().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Use((Strategy)null!).When<TimeoutException>().Retry(1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).WhenResult(static value => value < 0).Retry(1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Use((Strategy)null!).WhenResult(static value => value < 0).Retry(1);",
        };

        await AssertEachAsync(cases, "KEV007", "KEV004");
    }

    [Test]
    public async Task KEV007_Leaves_Consumed_And_Escaping_Clauses_Alone()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Or<TimeoutException>().CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Fallback(static (_, _) => default);",
            "_ = Shield.When<InvalidOperationException>().Fallback(static _ => default);",
            "_ = Shield.When<InvalidOperationException>().Use(static clause => (Strategy)null!).When<TimeoutException>().Retry(1);",
            "_ = Shield.For<int>().WhenResult(static value => value < 0).FallbackTo(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Use(static clause => (Strategy)null!).WhenResult(static value => value < 0).Retry(1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Retry(1);",
            "var clause = Shield.When<InvalidOperationException>(); _ = clause.Retry(1);",
            "var clause = Shield.When<InvalidOperationException>(); _ = clause.Or<TimeoutException>().Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Wrap(Shield.Empty).When<TimeoutException>().Retry(1);",
            "_ = Clause().Retry(1);",
            "_ = Shield.Empty.WhenAnyError().Retry(1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                "private static ShieldBuilder Clause() => Shield.When<InvalidOperationException>();");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV007_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string clause = "Shield.When<InvalidOperationException>().Or<TimeoutException>()";
        var source = $$"""
            public class TestSubject
            {
                public void Build() => {{clause}};
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeBodyAsync("""
            #pragma warning disable KEV007 // The clause is asserted on elsewhere.
            Shield.When<InvalidOperationException>();
            #pragma warning restore KEV007
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV007");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "This handling clause never reaches a reactive strategy, so it has no effect: "
            + "the ShieldBuilder it returns is discarded. Finish the clause with Retry, "
            + "CircuitBreaker, Hedge, Fallback, or Use, or remove it.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(clause, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(clause.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV008_Flags_Static_Instance_And_Builder_Chains_Used_As_Statements()
    {
        var cases = new[]
        {
            "Shield.Retry(3);",
            "Shield.CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "Shield.Empty.Retry(3);",
            "Shield.Empty.WithName(\"api\");",
            "Shield.Empty.For<int>();",
            "Shield.For<int>().Retry(3);",
            "Shield.When<InvalidOperationException>().Retry(3);",
            "Shield.For<int>().WhenResult(static value => value < 0).FallbackTo(0);",
            "Shield.When<InvalidOperationException>().Fallback(static _ => default);",
            "Shield.Compose(Shield.Empty, Shield.Empty);",
            "var shield = Shield.Empty; shield.Timeout(TimeSpan.FromSeconds(1));",
        };

        await AssertEachAsync(cases, "KEV008");
    }

    [Test]
    public async Task KEV008_Leaves_Used_Results_And_Executions_Alone()
    {
        var cases = new[]
        {
            "var shield = Shield.Retry(3);",
            "var shield = Shield.Empty; shield = shield.Retry(3);",
            "_ = Shield.Retry(3);",
            "Consume(Shield.Empty.Retry(3));",
            "Consume(Build());",
            "Shield.Empty.Execute(static _ => { });",
            "await Shield.Empty.ExecuteAsync(static _ => ValueTask.CompletedTask);",
            "_ = await Shield.For<int>().Retry(1).ExecuteAsync(static _ => new ValueTask<int>(1));",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                """
                private static Shield Build() => Shield.Retry(3);

                private static void Consume(Shield shield)
                {
                }
                """);
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV008_Defers_Discarded_Clause_Builders_To_KEV007()
    {
        var cases = new[]
        {
            "Shield.When<InvalidOperationException>();",
            "Shield.Empty.When<InvalidOperationException>().Or<TimeoutException>();",
            "Shield.For<int>().WhenResultIsDefault();",
        };

        await AssertEachAsync(cases, "KEV007", "KEV010");
    }

    [Test]
    public async Task KEV008_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string chain = "Shield.Empty.Retry(3)";
        var source = $$"""
            public class TestSubject
            {
                public void Build() => {{chain}};
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        var suppressed = await AnalyzeBodyAsync("""
            #pragma warning disable KEV008 // Construction is asserted on elsewhere.
            Shield.Empty.Retry(3);
            #pragma warning restore KEV008
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV008");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "'Retry' returns a new shield instead of changing this one, and its result is discarded "
            + "here, so this statement configures nothing. Assign the returned shield, or continue "
            + "the chain from it.");
        await Assert.That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(CreateSource(source).IndexOf(chain, StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo(chain.Length);
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV003_Flags_Unreachable_Reactive_Strategies()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().Retry(1).FallbackTo(0);",
            "_ = Shield.For<int>().Hedge(2, TimeSpan.Zero).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(2, TimeSpan.FromSeconds(1)).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.ConsecutiveFailures = 2).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.BreakDuration = TimeSpan.FromSeconds(1)).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.HandlesException = null).FallbackTo(0);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.OnStateChanged = _ => options.HandlesException = exception => exception is TimeoutException).FallbackTo(0);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1).FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).FallbackTo(0, static options => options.OnFallback = static _ => { });",
            "_ = Shield.Retry(1).Fallback(static _ => ValueTask.CompletedTask, static options => options.OnFallback = static _ => { });",
            "_ = Shield.For<int>().Retry(1).WhenAnyError().FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).When<InvalidOperationException>().Timeout(TimeSpan.Zero).WhenAnyError().FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).When<ArgumentException>().Timeout(TimeSpan.Zero).When<InvalidOperationException>().Timeout(TimeSpan.Zero).WhenAnyError().FallbackTo(0);",
            "var shield = Shield.For<int>().Retry(1); var alias = shield; _ = alias.FallbackTo(0);",
            "Shield<int>? shield = Shield.For<int>().Retry(1); _ = shield?.FallbackTo(0);",
            "_ = ShieldExtensions.Fallback(ShieldExtensions.Retry(Shield.Empty, 1), static _ => ValueTask.CompletedTask);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.Empty).FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.Timeout(TimeSpan.FromSeconds(1))).FallbackTo(0);",
            "_ = Shield<int>.Empty.Wrap(Shield.Retry(1)).FallbackTo(0);",
            "_ = Shield.Compose(Shield.Retry(1)).For<int>().FallbackTo(0);",
            "_ = Shield.Compose(Shield.Timeout(TimeSpan.FromSeconds(1)), Shield.Retry(1)).For<int>().FallbackTo(0);",
            "_ = Shield.Compose([Shield.Retry(1)]).For<int>().FallbackTo(0);",
            "var parts = new[] { Shield.Retry(1) }; _ = Shield.Compose(parts).For<int>().FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).Wrap(Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1))).FallbackTo(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).Wrap(Shield.Retry(1)).FallbackTo(0);",
            "_ = Shield.Compose(Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)), Shield.Retry(1)).For<int>().FallbackTo(0);",
            "_ = Shield.For<int>().Retry(1).When<ArgumentException>().Timeout(TimeSpan.Zero).Wrap(Shield.Empty).FallbackTo(0);",
            "_ = Shield.Compose(Shield.Retry(1).When<ArgumentException>().Timeout(TimeSpan.Zero), Shield.Empty).For<int>().FallbackTo(0);",
            "var retry = Shield.Retry(1); var fallback = Shield.For<int>().FallbackTo(0); _ = retry.Wrap(fallback);",
            "var retry = Shield.Retry(1); var fallback = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); _ = retry.Wrap(fallback);",
        };

        // Some cases replace a clause that only a timeout ever saw, which is exactly what KEV007
        // reports, and some chain a fallback behind a reactive strategy under one clause, which is
        // what KEV009 makes visible; the fallback reachability under test is unaffected by either.
        await AssertEachAsync(cases, "KEV003", "KEV007", "KEV009");
    }

    [Test]
    public async Task KEV003_Supports_Type_Aliases_And_Generic_Result_Shields()
    {
        var aliasDiagnostics = await AnalyzeSourceAsync("""
            using KShield = Kevlar.Shield;

            public class TestSubject
            {
                public Shield<int> Build() => KShield.For<int>().Retry(1).FallbackTo(0);
            }
            """);
        var genericDiagnostics = await AnalyzeSourceAsync("""
            public class TestSubject
            {
                public Shield<T> Build<T>() => Shield.For<T>().Retry(1).FallbackTo((T)default!);
            }
            """);

        await AssertRuleAsync(aliasDiagnostics, "KEV003");
        await AssertRuleAsync(genericDiagnostics, "KEV003");
    }

    [Test]
    public async Task KEV003_Skips_Reactive_Strategy_With_Local_Handling_Override()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options => options.HandlesException = exception => exception is TimeoutException).FallbackTo(0);");

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Recognizes_Compound_Local_Handling_Assignments()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options => options.HandlesException ??= exception => exception is TimeoutException).FallbackTo(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options => options.HandlesException += exception => exception is TimeoutException).FallbackTo(0);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV003_Skips_Fallback_With_Local_Handling_Override()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.For<int>().Retry(1).FallbackTo(0, options => options.HandlesException = exception => exception is TimeoutException);");

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Resolves_Reusable_Local_Handling_Configurators()
    {
        var diagnostics = await AnalyzeBodyAsync(
            """
            Action<CircuitBreakerOptions<int>> configure = ConfigureBreaker;
            _ = Shield.For<int>().CircuitBreaker(ConfigureBreaker).FallbackTo(0);
            _ = Shield.For<int>().CircuitBreaker(configure).FallbackTo(0);
            _ = Shield.For<int>().Retry(1).FallbackTo(0, ConfigureFallback);
            """,
            """
            private static void ConfigureBreaker(CircuitBreakerOptions<int> options) =>
                options.HandlesException = exception => exception is TimeoutException;

            private static void ConfigureFallback(FallbackOptions<int> options) =>
                options.HandlesException = exception => exception is TimeoutException;
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Follows_Source_Configurator_Helper_Calls()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.For<int>().CircuitBreaker(ConfigureBreaker).FallbackTo(0);",
            """
            private static void ConfigureBreaker(CircuitBreakerOptions<int> options) =>
                ApplyHandling(options);

            private static void ApplyHandling(CircuitBreakerOptions<int> options) =>
                options.HandlesException = exception => exception is TimeoutException;
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Propagates_Unknown_From_Nested_Configurator_Call()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Shield.For<int>().CircuitBreaker(ConfigureBreaker).FallbackTo(0);",
            """
            private static Action<CircuitBreakerOptions<int>> SharedConfigure { get; } =
                options => options.HandlesException = exception => exception is TimeoutException;

            private static void ConfigureBreaker(CircuitBreakerOptions<int> options) =>
                SharedConfigure(options);
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Skips_Opaque_Local_Handling_Configurator()
    {
        var diagnostics = await AnalyzeBodyAsync(
            "_ = Build(options => options.HandlesException = exception => exception is TimeoutException);",
            """
            private static Shield<int> Build(Action<CircuitBreakerOptions<int>> configure) =>
                Shield.For<int>()
                    .When<InvalidOperationException>()
                    .CircuitBreaker(configure)
                    .FallbackTo(0);
            """);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task KEV003_Skips_Valid_Or_Unknown_Compositions()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().FallbackTo(0).Retry(1);",
            "_ = Shield.For<int>().Retry(1).When<InvalidOperationException>().FallbackTo(0);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Retry(1).WhenAnyError().FallbackTo(0);",
            "_ = Shield.For<int>().When<ArgumentException>().Retry(1).When<InvalidOperationException>().Timeout(TimeSpan.Zero).CircuitBreaker(2, TimeSpan.FromSeconds(1)).WhenAnyError().FallbackTo(0);",
            "_ = Shield.For<int>().Timeout(TimeSpan.FromSeconds(1)).FallbackTo(0);",
            "var shield = CreateShield(); _ = shield.FallbackTo(0);",
            "var shield = Shield.For<int>().Retry(1); shield = Shield<int>.Empty; _ = shield.FallbackTo(0);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1).Wrap(Shield.Empty).FallbackTo(0);",
            "_ = Shield.Compose(Shield.When<InvalidOperationException>().Retry(1), Shield.Empty).For<int>().FallbackTo(0);",
            "var clause = Shield.For<int>().When<InvalidOperationException>().Timeout(TimeSpan.Zero); var outer = clause.Retry(1); _ = outer.Wrap(clause).FallbackTo(0);",
            "var clause = Shield.When<InvalidOperationException>().Timeout(TimeSpan.Zero); var outer = clause.Retry(1); _ = Shield.Compose(outer, clause).For<int>().FallbackTo(0);",
            "var parts = new[] { Shield.Retry(1) }; parts[0] = Shield.Empty; _ = Shield.Compose(parts).For<int>().FallbackTo(0);",
            "var builder = Shield.For<int>().When<InvalidOperationException>(); var retry = builder.Retry(1); _ = retry.Wrap(builder.Timeout(TimeSpan.Zero)).FallbackTo(0);",
            "var fallback = Shield.Empty.Fallback(static _ => ValueTask.CompletedTask); var retry = Shield.Retry(1); _ = fallback.Wrap(retry);",
            "var retry = Shield.When<InvalidOperationException>().Retry(1); var fallback = Shield.For<int>().When<TimeoutException>().FallbackTo(0); _ = retry.Wrap(fallback);",
            "var outer = Shield.Retry(1).When<InvalidOperationException>().Timeout(TimeSpan.Zero); var fallback = Shield.For<int>().When<InvalidOperationException>().FallbackTo(0); _ = outer.Wrap(fallback);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, "private static Shield<int> CreateShield() => Shield.For<int>().Retry(1);");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV009_Flags_Reactive_Strategies_That_Inherit_An_Earlier_Clause()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.WhenContext((HandlingEvent handling) => handling.Attempt == 0).Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Or<TimeoutException>().Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Retry(1).RetryForever(Backoff.None);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1).Hedge(2, TimeSpan.Zero);",
            "_ = Shield.For<int>().When<InvalidOperationException>().FallbackTo(0).Retry(1);",
            "_ = Shield.For<int>().WhenResultIsDefault().Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "var outer = Shield.When<InvalidOperationException>().Retry(1); _ = outer.CircuitBreaker(2, TimeSpan.FromSeconds(1));",
        };

        await AssertEachAsync(cases, "KEV009", DiagnosticSeverity.Info, "KEV010");
    }

    [Test]
    public async Task KEV009_Flags_Every_Strategy_After_The_First_Across_Proactive_Links()
    {
        var diagnostics = await AnalyzeBodyAsync(
            """
            _ = Shield.When<InvalidOperationException>()
                .Retry(1)
                .Timeout(TimeSpan.FromSeconds(1))
                .CircuitBreaker(2, TimeSpan.FromSeconds(1))
                .RateLimit(10, TimeSpan.FromSeconds(1))
                .RetryForever(Backoff.None);
            """);

        // The retry states the clause at its own call site; the breaker and the forever-retry
        // inherit it across the timeout and rate limit, which carry no clause of their own.
        await Assert.That(diagnostics.Length).IsEqualTo(2);
        await Assert.That(diagnostics).All(static diagnostic => diagnostic.Id == "KEV009");
        await Assert.That(MarkedText(diagnostics)).IsEquivalentTo(new[] { "CircuitBreaker", "RetryForever" });
    }

    private static string[] MarkedText(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(static diagnostic => diagnostic.Location.SourceTree!.ToString()
                .Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length))
            .ToArray();

    [Test]
    public async Task KEV009_Skips_Reset_Replaced_Absent_Overridden_And_Sealed_Clauses()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Retry(1);",
            "_ = Shield.Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Retry(1).WhenAnyError().CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Retry(1).When<TimeoutException>().CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.When<InvalidOperationException>().Timeout(TimeSpan.FromSeconds(1)).RateLimit(10, TimeSpan.FromSeconds(1)).ConcurrencyLimit(2).Retry(1);",
            "_ = Shield.When<InvalidOperationException>().Retry(1).Wrap(Shield.Empty).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.Compose(Shield.When<InvalidOperationException>().Retry(1), Shield.Empty).CircuitBreaker(2, TimeSpan.FromSeconds(1));",
            "_ = Shield.For<int>().When<InvalidOperationException>().Retry(1).CircuitBreaker(options => options.HandlesException = exception => exception is TimeoutException);",
            "_ = Shield.For<int>().When<InvalidOperationException>().CircuitBreaker(options => options.HandlesException = exception => exception is TimeoutException).Retry(1);",
            "_ = Shield.For<int>().When<InvalidOperationException>().Retry(1).CircuitBreaker(options => options.HandlesExceptionWithContext = handling => handling.Attempt == 0);",
            "_ = CreateShield().CircuitBreaker(2, TimeSpan.FromSeconds(1));",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                "private static Shield CreateShield() => Shield.When<InvalidOperationException>().Retry(1);");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV009_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string body = "_ = Shield.When<InvalidOperationException>().Retry(1).CircuitBreaker(2, TimeSpan.FromSeconds(1));";
        var diagnostics = await AnalyzeBodyAsync(body);
        var suppressed = await AnalyzeBodyAsync($"""
            #pragma warning disable KEV009 // The inherited clause is deliberate here.
            {body}
            #pragma warning restore KEV009
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV009");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "This strategy inherits the handling clause declared earlier in the chain "
            + "('When<InvalidOperationException>…'); only those exceptions or results count toward "
            + "it. Declare a new clause, or call 'WhenAnyError()' first, to give it different "
            + "handling.");

        // The hint marks only the inheriting strategy's name, not the whole chain.
        var span = diagnostic.Location.SourceSpan;
        await Assert.That(diagnostic.Location.SourceTree!.ToString().Substring(span.Start, span.Length))
            .IsEqualTo("CircuitBreaker");
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV010_Flags_Default_Result_Clauses_Written_For_A_Value_Type()
    {
        var cases = new[]
        {
            "_ = Shield.For<int>().WhenResultIsDefault().Retry(1);",
            "_ = Shield.For<bool>().WhenResultIsDefault().Retry(1);",
            "_ = Shield.For<TimeSpan>().WhenResultIsDefault().FallbackTo(TimeSpan.MaxValue);",
            "_ = Shield.For<int>().When<InvalidOperationException>().OrResultIsDefault().Retry(1);",
            "_ = Shield.For<int>().WhenResultIsDefault().Or<InvalidOperationException>().Retry(1);",
            "var clause = Shield.For<int>().WhenResultIsDefault(); _ = clause.Retry(1);",
        };

        await AssertEachAsync(cases, "KEV010", DiagnosticSeverity.Info);
    }

    [Test]
    public async Task KEV010_Skips_Reference_Types_Nullables_Generic_Results_And_Explicit_Values()
    {
        var cases = new[]
        {
            "_ = Shield.For<string>().WhenResultIsDefault().Retry(1);",
            "_ = Shield.For<string>().WhenResultIsNull().Retry(1);",
            "_ = Shield.For<string>().When<InvalidOperationException>().OrResultIsNull().Retry(1);",
            "_ = Shield.For<int?>().WhenResultIsDefault().Retry(1);",
            "_ = Shield.For<int>().WhenResult(0).Retry(1);",
            "_ = Build<int>();",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(
                body,
                // Generic code has no result to name but default(T), so the clause is all it can write.
                "private static Shield<T> Build<T>() => Shield.For<T>().WhenResultIsDefault().Retry(1);");
            await Assert.That(diagnostics).IsEmpty();
        }
    }

    [Test]
    public async Task KEV010_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string body = "_ = Shield.For<int>().WhenResultIsDefault().Retry(1);";
        var diagnostics = await AnalyzeBodyAsync(body);
        var suppressed = await AnalyzeBodyAsync($"""
            #pragma warning disable KEV010 // Zero really is the failure here.
            {body}
            #pragma warning restore KEV010
            """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV010");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "'WhenResultIsDefault' handles 'default(int)', which for a value type — 0, false, an "
            + "empty struct — is as often a legitimate result as a failure. Confirm that is "
            + "intended, or select the failing results with 'WhenResult'/'OrResult'.");

        // The hint marks only the clause's name, not the whole chain.
        await Assert.That(MarkedText(diagnostics)).IsEquivalentTo(new[] { "WhenResultIsDefault" });
        await Assert.That(suppressed).IsEmpty();
    }

    [Test]
    public async Task KEV011_Flags_Reactive_Strategies_With_Implicit_Default_Handling()
    {
        var cases = new[]
        {
            "_ = Shield.Retry(3);",
            "_ = Shield.For<int>().CircuitBreaker(3, TimeSpan.FromSeconds(1));",
            "_ = Shield.Empty.Hedge(2, TimeSpan.Zero);",
            "_ = Shield.For<int>().FallbackTo(0);",
            "var baseline = Shield.Empty; _ = baseline.RetryForever();",
            "_ = Shield.Empty.WhenAnyError().Wrap(Shield.Empty).Retry(1);",
            "_ = Shield.Compose(Shield.Empty.WhenAnyError(), Shield.Empty).Retry(1);",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, enableImplicitDefaultHandlingRule: true);
            await AssertRuleAsync(Without(diagnostics, "KEV006"), "KEV011", DiagnosticSeverity.Info);
        }
    }

    [Test]
    public async Task KEV011_Skips_Explicit_Ambient_Local_And_Reset_Handling()
    {
        var cases = new[]
        {
            "_ = Shield.When<InvalidOperationException>().Retry(3);",
            "_ = Shield.For<int>().WhenResult(-1).FallbackTo(0);",
            "_ = Shield.Retry(options => options.HandlesException = exception => exception is InvalidOperationException);",
            "_ = Shield.For<int>().CircuitBreaker(options => options.HandlesResult = value => value < 0);",
            "_ = Shield.When<InvalidOperationException>().Retry(1).WhenAnyError().Retry(1);",
            "_ = Shield.Timeout(TimeSpan.FromSeconds(1));",
        };

        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body, enableImplicitDefaultHandlingRule: true);
            await Assert.That(Without(diagnostics, "KEV009")).IsEmpty();
        }
    }

    [Test]
    public async Task KEV011_Diagnostic_Contract_And_Suppression_Are_Exact()
    {
        const string body = "_ = Shield.Retry(3);";
        var diagnostics = await AnalyzeBodyAsync(body, enableImplicitDefaultHandlingRule: true);
        var suppressed = await AnalyzeBodyAsync($"""
            #pragma warning disable KEV011 // Retrying all ordinary errors is deliberate.
            {body}
            #pragma warning restore KEV011
            """, enableImplicitDefaultHandlingRule: true);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var diagnostic = diagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("KEV011");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Info);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            "'Retry' uses Kevlar's default handling, which includes programming errors. Declare "
            + "a When clause or local HandlesException override when only expected failures should be handled.");
        await Assert.That(MarkedText(diagnostics)).IsEquivalentTo(new[] { "Retry" });
        await Assert.That(suppressed).IsEmpty();
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
            "_ = Shield.Hedge(2, TimeSpan.Zero).Execute(_ => 1); _ = Shield.For<int>().Retry(1).FallbackTo(0);",
            isGenerated: true);

        await Assert.That(unrelated).IsEmpty();
        await Assert.That(generated).IsEmpty();
    }

    private static async Task AssertEachAsync(
        IEnumerable<string> cases,
        string expectedRule,
        params string[] expectedCompanionRules)
    {
        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await AssertRuleAsync(Without(diagnostics, expectedCompanionRules), expectedRule);
        }
    }

    private static async Task AssertEachAsync(
        IEnumerable<string> cases,
        string expectedRule,
        DiagnosticSeverity expectedSeverity,
        params string[] expectedCompanionRules)
    {
        foreach (var body in cases)
        {
            var diagnostics = await AnalyzeBodyAsync(body);
            await AssertRuleAsync(Without(diagnostics, expectedCompanionRules), expectedRule, expectedSeverity);
        }
    }

    /// <summary>
    /// Drops rules a case is expected to trigger in addition to the rule under test — untyped
    /// hedging cases, for instance, always also report KEV006.
    /// </summary>
    private static ImmutableArray<Diagnostic> Without(
        ImmutableArray<Diagnostic> diagnostics,
        params string[] ruleIds) =>
        ruleIds.Length == 0
            ? diagnostics
            : diagnostics
                .Where(diagnostic => !ruleIds.Contains(diagnostic.Id, StringComparer.Ordinal))
                .ToImmutableArray();

    private static async Task AssertRuleAsync(
        ImmutableArray<Diagnostic> diagnostics,
        string expectedRule,
        DiagnosticSeverity expectedSeverity = DiagnosticSeverity.Warning)
    {
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo(expectedRule);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(expectedSeverity);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeBodyAsync(
        string body,
        string members = "",
        bool isGenerated = false,
        string assemblyName = "PipelineHazardAnalyzerTestSubject",
        bool enableImplicitDefaultHandlingRule = false) =>
        AnalyzeSourceAsync($$"""
            public class TestSubject
            {
                {{members}}

                public async Task RunAsync()
                {
                    {{body}}
                }
            }
            """, isGenerated, assemblyName: assemblyName, enableImplicitDefaultHandlingRule: enableImplicitDefaultHandlingRule);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(
        string declarations,
        bool isGenerated = false,
        bool allowCompilationErrors = false,
        string assemblyName = "PipelineHazardAnalyzerTestSubject",
        bool enableImplicitDefaultHandlingRule = false)
    {
        var source = CreateSource(declarations, isGenerated, enableImplicitDefaultHandlingRule);
        var compilation = CreateCompilation(source, assemblyName);
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (!allowCompilationErrors && errors.Length > 0)
        {
            throw new InvalidOperationException("Test source does not compile: " + string.Join("; ", errors.Select(static error => error.ToString())));
        }

        return await GetAnalyzerDiagnosticsAsync(compilation);
    }

    private static string CreateSource(
        string declarations,
        bool isGenerated = false,
        bool enableImplicitDefaultHandlingRule = false) =>
        (isGenerated ? "// <auto-generated/>\n" : string.Empty)
        + (enableImplicitDefaultHandlingRule ? string.Empty : "#pragma warning disable KEV011\n")
        + $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Kevlar;
            using Kevlar.Extensions.RateLimiting;

            {{declarations}}
            """;

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "PipelineHazardAnalyzerTestSubject")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(
                typeof(Kevlar.Extensions.RateLimiting.ShieldRateLimiterExtensions).Assembly.Location));
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

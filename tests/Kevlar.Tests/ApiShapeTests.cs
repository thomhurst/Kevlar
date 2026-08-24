using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Testing;

namespace Kevlar.Tests;

public class ApiShapeTests
{
    [Test]
    public async Task Exactly_One_Public_Type_Named_BackoffKind_Across_All_Assemblies()
    {
        var types = new[]
            {
                typeof(Shield).Assembly,
                typeof(ShieldDefinition).Assembly,
                typeof(ShieldDescriptor).Assembly,
            }
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(static type => type.Name == "BackoffKind")
            .ToArray();

        await Assert.That(types).HasSingleItem();
        await Assert.That(types[0].FullName).IsEqualTo("Kevlar.BackoffKind");
    }

    [Test]
    public async Task BackoffKind_Is_Unambiguous_With_DI_And_Testing_Usings()
    {
        var compilation = CreateCompilation(
            """
            using Kevlar;
            using Kevlar.Extensions.DependencyInjection;
            using Kevlar.Testing;

            public static class Consumer
            {
                public static BackoffKind Kind => BackoffKind.Exponential;
            }
            """);

        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task No_Public_Member_Named_MaxQueue_Remains()
    {
        var assemblies = new[]
        {
            typeof(Shield).Assembly,
            typeof(Extensions.DependencyInjection.ShieldDefinition).Assembly,
            typeof(Extensions.Http.StandardHedgingShieldOptions).Assembly,
        };
        var legacyMembers = assemblies
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .SelectMany(static type => type.GetMembers())
            .Where(static member => member.Name == "MaxQueue")
            .Select(static member => $"{member.DeclaringType}.{member.Name}")
            .ToArray();

        await Assert.That(legacyMembers).IsEmpty();
    }

    [Test]
    public async Task Typed_CircuitBreaker_Requires_Typed_Options_Configurator()
    {
        var compilation = CreateCompilation(
            """
            using Kevlar;
            using System;

            public static class Consumer
            {
                public static void Build()
                {
                    Action<CircuitBreakerOptions> untyped = static options => options.ConsecutiveFailures = 2;
                    Action<CircuitBreakerOptions<int>> typed = static options => options.ConsecutiveFailures = 2;
                    _ = Shield.For<int>().CircuitBreaker(typed);
                    _ = Shield.For<int>().CircuitBreaker(untyped);
                }
            }
            """);

        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0].Id).IsEqualTo("CS1503");
    }

    [Test]
    public async Task FallbackTo_Accepts_Null_Without_Overload_Ambiguity()
    {
        var compilation = CreateCompilation(
            """
            using Kevlar;
            using System.Net.Http;

            public static class Consumer
            {
                public static void Build()
                {
                    _ = Shield.For<string?>().FallbackTo(null);
                    _ = Shield.For<int?>().FallbackTo(null);
                    _ = Shield.For<HttpResponseMessage?>().FallbackTo(null);
                    _ = Shield.For<string?>().When<System.Exception>().FallbackTo(null);
                }
            }
            """);

        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Legacy_Fallback_Null_Remains_Ambiguous_Between_Delegate_Overloads()
    {
        var compilation = CreateCompilation(
            """
            using Kevlar;

            public static class Consumer
            {
                public static void Build() => _ = Shield.For<string?>().Fallback(null);
            }
            """);

        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0].Id).IsEqualTo("CS0121");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ShieldDefinition).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ShieldDescriptor).Assembly.Location));

        return CSharpCompilation.Create(
            "FallbackApiShape",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

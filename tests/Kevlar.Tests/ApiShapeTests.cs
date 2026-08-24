using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kevlar.Tests;

public class ApiShapeTests
{
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
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location));

        return CSharpCompilation.Create(
            "FallbackApiShape",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

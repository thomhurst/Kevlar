using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kevlar.ApiVerifier;

internal static class Program
{
    private const string NullableHeader = "#nullable enable";

    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var expected = ReadExpectedApi(options.BaselinePaths);
            var actual = ReadAssemblyApi(options.AssemblyPath, options.ReferenceRoots, options.TargetFramework);
            var missing = expected.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var unexpected = actual.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length == 0 && unexpected.Length == 0)
            {
                Console.WriteLine($"{Path.GetFileName(options.AssemblyPath)}: {actual.Count} public APIs match shipped baselines.");
                return 0;
            }

            WriteDifference("Missing from assembly", missing);
            WriteDifference("Missing from shipped baselines", unexpected);
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static HashSet<string> ReadExpectedApi(IEnumerable<string> baselinePaths)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in baselinePaths)
        {
            foreach (var line in File.ReadLines(path))
            {
                var api = line.Trim();
                if (api.Length == 0 || api == NullableHeader)
                {
                    continue;
                }

                if (api.StartsWith("*REMOVED*", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Shipped baseline '{path}' contains a removed API entry.");
                }

                if (!expected.Add(api))
                {
                    throw new InvalidOperationException($"Duplicate shipped API '{api}' in '{path}'.");
                }
            }
        }

        return expected;
    }

    private static HashSet<string> ReadAssemblyApi(
        string assemblyPath,
        IReadOnlyList<string> referenceRoots,
        string targetFramework)
    {
        var references = CreateReferences(assemblyPath, referenceRoots, targetFramework);
        var targetReference = references.Single(reference =>
            string.Equals(reference.FilePath, assemblyPath, StringComparison.OrdinalIgnoreCase));
        var compilation = CSharpCompilation.Create(
            "Kevlar.ApiVerifier.Metadata",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var assembly = compilation.GetAssemblyOrModuleSymbol(targetReference) as IAssemblySymbol
            ?? throw new InvalidOperationException($"Could not load metadata symbols from '{assemblyPath}'.");
        var format = LoadPublicApiFormat();
        var api = new HashSet<string>(StringComparer.Ordinal);
        VisitNamespace(assembly.GlobalNamespace, format, api);
        return api;
    }

    private static List<PortableExecutableReference> CreateReferences(
        string assemblyPath,
        IReadOnlyList<string> referenceRoots,
        string targetFramework)
    {
        var candidates = new List<string> { assemblyPath };
        foreach (var root in referenceRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            candidates.AddRange(Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
                .Where(path => IsCompatibleReferencePath(path, targetFramework)));
        }

        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (!string.IsNullOrWhiteSpace(platformAssemblies))
        {
            candidates.AddRange(platformAssemblies.Split(Path.PathSeparator));
        }

        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                var name = AssemblyName.GetAssemblyName(fullPath).Name;
                if (name is not null && !selected.ContainsKey(name))
                {
                    selected.Add(name, fullPath);
                }
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
        }

        return selected.Values.Select(static path => MetadataReference.CreateFromFile(path)).ToList();
    }

    private static bool IsCompatibleReferencePath(string path, string targetFramework)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains($"/{targetFramework}/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"release_{targetFramework}", StringComparison.OrdinalIgnoreCase)
            || !normalized.Contains("release_net", StringComparison.OrdinalIgnoreCase);
    }

    private static SymbolDisplayFormat LoadPublicApiFormat()
    {
        var analyzerPath = Path.Combine(AppContext.BaseDirectory, "Microsoft.CodeAnalysis.PublicApiAnalyzers.dll");
        var analyzer = Assembly.LoadFrom(analyzerPath);
        var analyzerType = analyzer.GetType(
            "Microsoft.CodeAnalysis.PublicApiAnalyzers.DeclarePublicApiAnalyzer",
            throwOnError: true)!;
        return (SymbolDisplayFormat)(analyzerType.GetField(
            "s_publicApiFormatWithNullability",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException("PublicApiAnalyzers format field was not found."));
    }

    private static void VisitNamespace(
        INamespaceSymbol namespaceSymbol,
        SymbolDisplayFormat format,
        HashSet<string> api)
    {
        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            VisitNamespace(childNamespace, format, api);
        }

        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            VisitType(type, format, api);
        }
    }

    private static void VisitType(INamedTypeSymbol type, SymbolDisplayFormat format, HashSet<string> api)
    {
        if (IsTracked(type))
        {
            api.Add(FormatApi(type, format));
        }

        foreach (var nestedType in type.GetTypeMembers())
        {
            VisitType(nestedType, format, api);
        }

        foreach (var member in type.GetMembers())
        {
            if (!IsTracked(member) || !ShouldInclude(member))
            {
                continue;
            }

            api.Add(FormatApi(member, format));
        }
    }

    private static bool ShouldInclude(ISymbol symbol) => symbol switch
    {
        IFieldSymbol => true,
        IEventSymbol => true,
        IMethodSymbol { ContainingType.TypeKind: TypeKind.Delegate } method =>
            method.MethodKind == MethodKind.DelegateInvoke,
        IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType.TypeKind: TypeKind.Enum } => false,
        IMethodSymbol { MethodKind: MethodKind.StaticConstructor } => false,
        IMethodSymbol { MethodKind: MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise } => false,
        IMethodSymbol => true,
        _ => false,
    };

    private static bool IsTracked(ISymbol symbol)
    {
        if (!IsResultantlyPublic(symbol))
        {
            return false;
        }

        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal
                && current.ContainingType is { } containingType
                && !CanBeExtended(containingType))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsResultantlyPublic(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (
                Accessibility.Public or
                Accessibility.Protected or
                Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanBeExtended(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Interface
        || type is { TypeKind: TypeKind.Class, IsSealed: false };

    private static string FormatApi(ISymbol symbol, SymbolDisplayFormat format)
    {
        var text = symbol.ToDisplayString(format);
        var resultType = symbol switch
        {
            IMethodSymbol method => method.ReturnType,
            IEventSymbol @event => @event.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
        return resultType is null
            ? text
            : $"{text} -> {resultType.ToDisplayString(format)}";
    }

    private static void WriteDifference(string title, IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        Console.Error.WriteLine($"{title} ({entries.Count}):");
        foreach (var entry in entries)
        {
            Console.Error.WriteLine($"  {entry}");
        }
    }

    private sealed record Options(
        string AssemblyPath,
        string TargetFramework,
        IReadOnlyList<string> BaselinePaths,
        IReadOnlyList<string> ReferenceRoots)
    {
        public static Options Parse(string[] args)
        {
            string? assemblyPath = null;
            string? targetFramework = null;
            var baselinePaths = new List<string>();
            var referenceRoots = new List<string>();
            for (var index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for '{args[index]}'.");
                }

                switch (args[index])
                {
                    case "--assembly":
                        assemblyPath = Path.GetFullPath(args[index + 1]);
                        break;
                    case "--tfm":
                        targetFramework = args[index + 1];
                        break;
                    case "--baseline":
                        baselinePaths.Add(Path.GetFullPath(args[index + 1]));
                        break;
                    case "--reference-root":
                        referenceRoots.Add(Path.GetFullPath(args[index + 1]));
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[index]}'.");
                }
            }

            if (assemblyPath is null || targetFramework is null || baselinePaths.Count == 0)
            {
                throw new ArgumentException("Required arguments: --assembly, --tfm, and at least one --baseline.");
            }

            return new Options(assemblyPath, targetFramework, baselinePaths, referenceRoots);
        }
    }
}

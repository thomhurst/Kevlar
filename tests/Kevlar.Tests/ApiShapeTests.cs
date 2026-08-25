using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Kevlar.Extensions.RateLimiting;
using Kevlar.Chaos;
using Kevlar.Testing;
using System.Reflection;

namespace Kevlar.Tests;

public class ApiShapeTests
{
    [Test]
    public async Task AddShield_Overloads_Are_Symmetric_After_VoidShield_Fold()
    {
        static string Shape(MethodInfo method) =>
            string.Join(",", method.GetParameters().Skip(2).Select(static parameter => parameter.Name));

        var methods = typeof(KevlarServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static);
        var addShieldMethods = methods.Where(static method => method.Name == "AddShield").ToArray();
        var untypedShapes = addShieldMethods
            .Where(static method => !method.IsGenericMethodDefinition)
            .Select(Shape)
            .ToArray();
        var typedShapes = addShieldMethods
            .Where(static method => method.IsGenericMethodDefinition)
            .Select(Shape)
            .ToArray();
        var reloadingMethods = methods
            .Where(static method => method.Name == "AddReloadingShield")
            .ToArray();

        await Assert.That(typedShapes).IsEquivalentTo(untypedShapes);
        var untypedReloadShapes = reloadingMethods
            .Where(static method => !method.GetGenericArguments().Any(
                static argument => argument.Name == "TResult"))
            .Select(Shape)
            .ToArray();
        var typedReloadShapes = reloadingMethods
            .Where(static method => method.GetGenericArguments().Any(
                static argument => argument.Name == "TResult"))
            .Select(Shape)
            .ToArray();

        await Assert.That(typedReloadShapes).IsEquivalentTo(untypedReloadShapes);
        await Assert.That(methods.Where(static method => method.Name == "AddVoidShield")).IsEmpty();
    }

    [Test]
    public async Task Strategy_Event_Structs_Have_One_Context_Contract()
    {
        var eventTypes = GetStrategyEventTypes()
            .Where(static type => type != typeof(CallbackErrorEvent))
            .ToArray();

        await Assert.That(eventTypes.Length).IsGreaterThan(0);
        foreach (var eventType in eventTypes)
        {
            await Assert.That(eventType.IsDefined(
                typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute),
                inherit: false)).IsTrue();
            await Assert.That(eventType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(static constructor => constructor.IsAssembly)).IsTrue();

            var contextProperty = eventType.GetProperty(
                "Context",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            await Assert.That(contextProperty).IsNotNull();
            await Assert.That(contextProperty!.PropertyType).IsEqualTo(typeof(KevlarContext));

            var concreteType = eventType.IsGenericTypeDefinition
                ? eventType.MakeGenericType(
                    Enumerable.Repeat(typeof(int), eventType.GetGenericArguments().Length).ToArray())
                : eventType;
            var defaultEvent = Activator.CreateInstance(concreteType);
            var concreteContextProperty = concreteType.GetProperty(
                "Context",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
            var failure = await Assert.That(() => concreteContextProperty.GetValue(defaultEvent))
                .Throws<TargetInvocationException>();
            await Assert.That(failure!.InnerException).IsTypeOf<InvalidOperationException>();
        }
    }

    [Test]
    public async Task Event_Type_Names_Share_Strategy_Prefix()
    {
        var prefixes = new[]
        {
            "CircuitBreaker",
            "Callback",
            "Chaos",
            "ConcurrencyLimit",
            "Fallback",
            "Hedge",
            "KevlarTelemetry",
            "RateLimit",
            "RateLimiter",
            "Retry",
            "Timeout",
        };
        var eventTypes = GetStrategyEventTypes();

        await Assert.That(eventTypes.All(type => prefixes.Any(prefix =>
            type.Name.StartsWith(prefix, StringComparison.Ordinal)))).IsTrue();
        await Assert.That(eventTypes.Any(static type =>
            type.Name.StartsWith("CircuitState", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Public_Surface_Uses_One_Hedge_Stem()
    {
        var assemblies = new[]
        {
            typeof(Shield).Assembly,
            typeof(ShieldDefinition).Assembly,
            typeof(StandardHedgeShieldOptions).Assembly,
            typeof(ShieldDescriptor).Assembly,
        };
        var legacyNames = assemblies
            .SelectMany(static assembly => assembly.ExportedTypes)
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
            typeof(Extensions.Http.StandardHedgeShieldOptions).Assembly,
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
    public async Task Typed_Hedge_ActionGenerator_Requires_The_Matching_Result_Type()
    {
        var compilation = CreateCompilation(
            """
            using Kevlar;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Consumer
            {
                public static void Build()
                {
                    Func<HedgeActionGeneratorEvent<string>, Func<CancellationToken, ValueTask<string>>?> correct =
                        static _ => null;
                    Func<HedgeActionGeneratorEvent<string>, Func<CancellationToken, ValueTask<int>>?> wrong =
                        static _ => null;
                    var options = new HedgeOptions<string> { ActionGenerator = correct };
                    options.ActionGenerator = wrong;
                }
            }
            """);

        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0].Id).IsEqualTo("CS0029");
    }

    [Test]
    public async Task Void_Fallback_Preserves_The_Shield_Static_Type()
    {
        var compilation = CreateCompilation(
            """
            using Kevlar;
            using System.Threading.Tasks;

            public static class Consumer
            {
                public static void Build()
                {
                    Shield shield = Shield.Retry(3)
                        .Fallback(static _ => ValueTask.CompletedTask);
                }
            }
            """);

        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task VoidShield_Type_Family_Is_Not_Publicly_Exposed()
    {
        var assembly = typeof(Shield).Assembly;

        await Assert.That(assembly.GetType("Kevlar.VoidShield")).IsNull();
        await Assert.That(assembly.GetType("Kevlar.VoidShieldBuilder")).IsNull();
        await Assert.That(assembly.GetType("Kevlar.PartitionedVoidShield`1")).IsNull();
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
    public async Task Reloading_Shield_Legacy_Callback_Literals_Remain_Unambiguous()
    {
        var compilation = CreateCompilation(
            """
            using Kevlar.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public static class Consumer
            {
                public static void Build(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddReloadingShield("untyped", configuration, null);
                    services.AddReloadingShield<int>("typed", configuration, null);
                    services.AddReloadingShield("untyped-default", configuration, default);
                    services.AddReloadingShield<int>("typed-default", configuration, default);

                    var options = new ReloadingShieldOptions();
                    services.AddReloadingShield("untyped-options", options, configuration);
                    services.AddReloadingShield<int>("typed-options", options, configuration);
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

    [Test]
    public async Task Handling_Predicates_Remain_Unambiguous_For_CSharp_12_Consumers()
    {
        var compilation = CreateCompilation(
            """
            using Kevlar;

            public static class Consumer
            {
                public static void Build()
                {
                    _ = Shield.When(_ => true);
                    _ = Shield.Empty.When(_ => true);
                    _ = Shield.For<int>().When(_ => true);
                    _ = Shield.For<int>().WhenResult(_ => true);
                    _ = Shield.When<System.Exception>().Or(_ => true);
                    _ = Shield.For<int>().When<System.Exception>().Or(_ => true).OrResult(_ => true);

                    _ = Shield.WhenContext(handling => handling.Attempt == 0);
                    _ = Shield.For<int>().WhenResultContext(handling => handling.Attempt == 0);
                }
            }
            """,
            LanguageVersion.CSharp12);

        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ShieldDefinition).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ShieldDescriptor).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.Configuration.IConfiguration).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location));

        return CSharpCompilation.Create(
            "FallbackApiShape",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(languageVersion))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Type[] GetStrategyEventTypes() =>
        new[]
        {
            typeof(Shield).Assembly,
            typeof(ChaosEvent).Assembly,
            typeof(RateLimiterAdapterRejectedEvent).Assembly,
        }
            .Distinct()
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(static type =>
                type.IsValueType
                && type.Name.Contains("Event", StringComparison.Ordinal)
                && !type.Name.StartsWith("HandlingEvent", StringComparison.Ordinal))
            .ToArray();
}

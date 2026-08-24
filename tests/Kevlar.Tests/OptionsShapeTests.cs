using System.Reflection;

namespace Kevlar.Tests;

public class OptionsShapeTests
{
    private static readonly (Type Untyped, Type Typed)[] TypedPairs =
    [
        (typeof(RetryOptions), typeof(RetryOptions<int>)),
        (typeof(CircuitBreakerOptions), typeof(CircuitBreakerOptions<int>)),
        (typeof(HedgeOptions), typeof(HedgeOptions<int>)),
        (typeof(FallbackOptions), typeof(FallbackOptions<int>)),
    ];

    [Test]
    public async Task All_Options_Types_Are_Sealed_Siblings()
    {
        var optionTypes = typeof(Shield).Assembly.GetExportedTypes()
            .Where(static type => type.IsClass && type.Name.Contains("Options", StringComparison.Ordinal))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        foreach (var optionType in optionTypes)
        {
            await Assert.That(optionType.IsSealed).IsTrue();
            await Assert.That(optionType.BaseType).IsEqualTo(typeof(object));
        }
    }

    [Test]
    public async Task Typed_And_Untyped_Options_Share_Property_Names()
    {
        foreach (var (untypedType, typedType) in TypedPairs)
        {
            var typedNames = typedType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            var missingNames = untypedType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(static property => property.Name)
                .Where(name => !typedNames.Contains(name))
                .ToArray();

            await Assert.That(missingNames).IsEmpty();
        }
    }

    [Test]
    public async Task Typed_And_Untyped_Options_Share_Scalar_Defaults()
    {
        foreach (var (untypedType, typedType) in TypedPairs)
        {
            var untyped = Activator.CreateInstance(untypedType)!;
            var typed = Activator.CreateInstance(typedType)!;

            foreach (var untypedProperty in untypedType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(IsScalar))
            {
                var typedProperty = typedType.GetProperty(untypedProperty.Name)!;
                await Assert.That(typedProperty.GetValue(typed))
                    .IsEqualTo(untypedProperty.GetValue(untyped));
            }
        }
    }

    private static bool IsScalar(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(decimal) ||
               type == typeof(string) ||
               type == typeof(TimeSpan) ||
               type == typeof(Backoff);
    }
}

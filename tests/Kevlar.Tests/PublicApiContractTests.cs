using System.Reflection;

namespace Kevlar.Tests;

public class PublicApiContractTests
{
    private const BindingFlags DeclaredPublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private const BindingFlags AllPublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    [Test]
    public async Task No_Public_Type_Without_Public_Members()
    {
        var emptyTypes = typeof(Shield).Assembly
            .GetExportedTypes()
            .Where(static type => !type.IsEnum && !typeof(Attribute).IsAssignableFrom(type))
            .Where(static type => type
                .GetMembers(AllPublicMembers)
                .All(static member => member.MemberType == MemberTypes.Constructor
                    || member.DeclaringType == typeof(object)))
            .Select(static type => type.FullName!)
            .Order()
            .ToArray();

        await Assert.That(emptyTypes).IsEmpty();
    }

    [Test]
    public async Task Public_Signatures_Do_Not_Reference_Internal_Types()
    {
        var leaks = typeof(Shield).Assembly
            .GetExportedTypes()
            .SelectMany(GetSignatureTypes)
            .Where(static signature => !IsPubliclyVisible(signature.Type))
            .Select(static signature => $"{signature.Member}: {signature.Type}")
            .Order()
            .ToArray();

        await Assert.That(leaks).IsEmpty();
    }

    private static IEnumerable<(MemberInfo Member, Type Type)> GetSignatureTypes(Type type)
    {
        foreach (var constructor in type.GetConstructors(DeclaredPublicMembers))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return (constructor, parameter.ParameterType);
            }
        }

        foreach (var method in type.GetMethods(DeclaredPublicMembers))
        {
            yield return (method, method.ReturnType);

            foreach (var parameter in method.GetParameters())
            {
                yield return (method, parameter.ParameterType);
            }
        }

        foreach (var property in type.GetProperties(DeclaredPublicMembers))
        {
            yield return (property, property.PropertyType);

            foreach (var parameter in property.GetIndexParameters())
            {
                yield return (property, parameter.ParameterType);
            }
        }

        foreach (var field in type.GetFields(DeclaredPublicMembers))
        {
            yield return (field, field.FieldType);
        }

        foreach (var @event in type.GetEvents(DeclaredPublicMembers))
        {
            yield return (@event, @event.EventHandlerType!);
        }
    }

    private static bool IsPubliclyVisible(Type type)
    {
        if (type.IsGenericParameter)
        {
            return true;
        }

        if (type.HasElementType)
        {
            return IsPubliclyVisible(type.GetElementType()!);
        }

        return type.IsVisible && (!type.IsGenericType || type.GetGenericArguments().All(IsPubliclyVisible));
    }
}

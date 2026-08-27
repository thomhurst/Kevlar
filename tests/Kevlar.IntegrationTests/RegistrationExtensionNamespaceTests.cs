using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.IntegrationTests;

public class RegistrationExtensionNamespaceTests
{
    [Test]
    public async Task Registration_Extensions_Use_DependencyInjection_Namespace()
    {
        var assemblies = new[]
        {
            typeof(KevlarServiceCollectionExtensions).Assembly,
            typeof(ShieldHttpClientBuilderExtensions).Assembly,
            typeof(KevlarLoggingServiceCollectionExtensions).Assembly,
            typeof(ShieldGrpcClientBuilderExtensions).Assembly,
        };
        var registrationExtensionTypes = assemblies
            .Distinct()
            .SelectMany(static assembly => assembly.ExportedTypes)
            .Where(static type => type.IsAbstract && type.IsSealed)
            .Where(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(static method => method.IsDefined(typeof(ExtensionAttribute), inherit: false)
                    && method.GetParameters() is [{ ParameterType: var receiver }, ..]
                    && (receiver == typeof(IServiceCollection)
                        || receiver == typeof(IHttpClientBuilder))))
            .ToArray();

        await Assert.That(registrationExtensionTypes.Length).IsGreaterThanOrEqualTo(4);
        await Assert.That(registrationExtensionTypes
                .Select(static type => type.Namespace ?? string.Empty)
                .Distinct())
            .IsEquivalentTo(["Microsoft.Extensions.DependencyInjection"]);
    }
}

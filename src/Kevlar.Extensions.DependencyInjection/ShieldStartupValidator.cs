using Microsoft.Extensions.Options;

namespace Kevlar.Extensions.DependencyInjection;

// A private options type joins the Generic Host's existing startup validation phase.
internal sealed class ShieldStartupOptions
{
    public ShieldStartupOptions() { }
}

internal sealed class ShieldStartupValidator : IValidateOptions<ShieldStartupOptions>
{
    private readonly KevlarRegistry _registry;
    private readonly IEnumerable<ShieldRegistration> _registrations;

    public ShieldStartupValidator(KevlarRegistry registry, IEnumerable<ShieldRegistration> registrations)
    {
        _registry = registry;
        _registrations = registrations;
    }

    public ValidateOptionsResult Validate(string? name, ShieldStartupOptions options)
    {
        foreach (var registration in _registrations)
        {
            try
            {
                _registry.ValidateRegistration(registration);
            }
            catch (Exception exception)
            {
                var resultType = registration.ResultType is { } type ? $" (result type '{type}')" : string.Empty;
                throw new KevlarConfigurationException(
                    $"Startup validation failed for shield '{registration.Name}'{resultType}: {exception.Message}",
                    exception);
            }
        }

        return ValidateOptionsResult.Success;
    }
}

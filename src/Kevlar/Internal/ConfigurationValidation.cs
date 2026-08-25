using System.Globalization;

namespace Kevlar.Internal;

internal static class ConfigurationValidation
{
    public static void ThrowIf<T>(
        bool invalid,
        Type optionsType,
        string propertyName,
        T value,
        string requirement)
    {
        if (!invalid)
        {
            return;
        }

        var tick = optionsType.Name.IndexOf('`');
        var optionsName = tick < 0 ? optionsType.Name : optionsType.Name[..tick];
        var formattedValue = value switch
        {
            null => "null",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        throw new KevlarConfigurationException(
            $"{optionsName}.{propertyName} {requirement} (was {formattedValue}).");
    }
}

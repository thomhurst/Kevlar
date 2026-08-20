using System.Diagnostics.CodeAnalysis;

namespace Kevlar.Internal;

internal static class Throw
{
    public static void IfNull([NotNull] object? argument, string parameterName)
    {
        if (argument is null)
        {
            throw new ArgumentNullException(parameterName);
        }
    }

    public static void IfOutOfRange(bool outOfRange, string parameterName, string message)
    {
        if (outOfRange)
        {
            throw new ArgumentOutOfRangeException(parameterName, message);
        }
    }
}

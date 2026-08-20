using System.Globalization;

namespace Kevlar.Internal;

/// <summary>Compact formatting shared by the <c>Describe</c>/<c>ToString</c> implementations.</summary>
internal static class DescribeHelper
{
    /// <summary>Formats a duration tersely: 250ms, 1.5s, 30s, 2m, 1h.</summary>
    public static string Time(TimeSpan value)
    {
        if (value == System.Threading.Timeout.InfiniteTimeSpan)
        {
            return "infinite";
        }

        if (value == TimeSpan.Zero)
        {
            return "0s";
        }

        if (value < TimeSpan.FromSeconds(1))
        {
            return Number(value.TotalMilliseconds) + "ms";
        }

        if (value < TimeSpan.FromMinutes(1))
        {
            return Number(value.TotalSeconds) + "s";
        }

        if (value < TimeSpan.FromHours(1))
        {
            return Number(value.TotalMinutes) + "m";
        }

        return Number(value.TotalHours) + "h";
    }

    private static string Number(double value) =>
        (Math.Round(value, 1)).ToString("0.#", CultureInfo.InvariantCulture);
}

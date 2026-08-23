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

    /// <summary>Joins the terms of one handling clause into <c>A | B | C</c>.</summary>
    public static string Clause(string[] terms) => string.Join(" | ", terms);

    /// <summary>Renders a result value inside a handling clause description.</summary>
    public static string Value<TResult>(TResult value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    private static string Number(double value) =>
        (Math.Round(value, 1)).ToString("0.#", CultureInfo.InvariantCulture);
}

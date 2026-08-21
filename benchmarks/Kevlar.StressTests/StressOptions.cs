using System.Globalization;

namespace Kevlar.StressTests;

internal sealed record StressOptions(TimeSpan Duration, TimeSpan Warmup, int Workers, string OutputPath)
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultWarmup = TimeSpan.FromSeconds(2);

    public static StressOptions Parse(string[] args)
    {
        var duration = DefaultDuration;
        var warmup = DefaultWarmup;
        var workers = Environment.ProcessorCount;
        var outputPath = Path.Combine("artifacts", "stress", "stress-results.json");

        for (var index = 0; index < args.Length; index++)
        {
            var value = index + 1 < args.Length ? args[index + 1] : null;
            switch (args[index])
            {
                case "--duration" when value is not null:
                    duration = ParseDuration(value, "--duration");
                    index++;
                    break;
                case "--warmup" when value is not null:
                    warmup = ParseDuration(value, "--warmup", allowZero: true);
                    index++;
                    break;
                case "--workers" when value is not null:
                    workers = int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
                    index++;
                    break;
                case "--output" when value is not null:
                    outputPath = value;
                    index++;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
            }
        }

        if (duration < TimeSpan.FromMilliseconds(2))
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Duration must be at least 2 ms.");
        }

        if (workers < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Workers must be at least 1.");
        }

        return new StressOptions(duration, warmup, workers, outputPath);
    }

    private static TimeSpan ParseDuration(string value, string option, bool allowZero = false)
    {
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration)
            || duration < TimeSpan.Zero
            || (!allowZero && duration == TimeSpan.Zero))
        {
            throw new ArgumentException($"{option} must be a valid positive duration.");
        }

        return duration;
    }
}

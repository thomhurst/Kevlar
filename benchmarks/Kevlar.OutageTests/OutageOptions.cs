using System.Globalization;

namespace Kevlar.OutageTests;

internal sealed record OutageOptions(
    int PhaseSeconds = 30,
    int RequestsPerSecond = 100,
    int MaximumInFlight = 512,
    string OutputPath = "artifacts/outage/outage-results.json")
{
    internal int ArrivalsPerPhase => checked(PhaseSeconds * RequestsPerSecond);
    internal int TotalArrivals => checked(ArrivalsPerPhase * 4);
    internal const int HealthyMilliseconds = 5;
    internal const int SlowMilliseconds = 300;
    internal const int AlternateMilliseconds = 30;
    internal const int FailureMilliseconds = 20;
    internal const int CleanupMilliseconds = 20;
    internal const int HedgeMilliseconds = 20;
    internal const int BreakMilliseconds = 500;
    internal const int Concurrency = 16;
    internal const int QueueLimit = 16;

    internal static OutageOptions Parse(string[] args)
    {
        var options = new OutageOptions();
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length) { throw new ArgumentException($"Missing value for {args[index]}."); }
            var value = args[index + 1];
            options = args[index] switch
            {
                "--phase-seconds" => options with { PhaseSeconds = int.Parse(value, CultureInfo.InvariantCulture) },
                "--rate" => options with { RequestsPerSecond = int.Parse(value, CultureInfo.InvariantCulture) },
                "--max-inflight" => options with { MaximumInFlight = int.Parse(value, CultureInfo.InvariantCulture) },
                "--output" => options with { OutputPath = value },
                _ => throw new ArgumentException($"Unknown argument {args[index]}.")
            };
        }
        if (options.PhaseSeconds is < 1 or > 300 || options.RequestsPerSecond is < 1 or > 10000
            || options.MaximumInFlight is < 1 or > 10000 || options.TotalArrivals > 1_000_000)
        {
            throw new ArgumentException("Use 1–300 phase seconds, 1–10000 requests/s and in-flight limit, and at most 1000000 arrivals per scenario.");
        }
        return options;
    }
}

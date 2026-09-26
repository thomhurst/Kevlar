namespace Kevlar.OutageTests;

internal sealed class RequestMeasurement(int id, double scheduledSeconds)
{
    internal int Id { get; } = id;
    internal double ScheduledSeconds { get; } = scheduledSeconds;
    internal double StartedSeconds;
    internal double CompletedSeconds;
    internal string Outcome = "pending";
    internal int Attempts;
}

internal sealed class AttemptMeasurement(RequestMeasurement request, double startedSeconds)
{
    internal RequestMeasurement Request { get; } = request;
    internal double StartedSeconds { get; } = startedSeconds;
    internal double CompletedSeconds;
    internal bool Cancelled;
    internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal static class Measurements
{
    internal static readonly string[] PhaseNames = ["healthy", "slowdown", "outage", "recovery"];

    internal static int Phase(double seconds, int phaseSeconds) => Math.Clamp((int)(seconds / phaseSeconds), 0, 3);

    internal static double? Percentile(IEnumerable<double> values, double percentile)
    {
        if (percentile is <= 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(percentile)); }
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];
    }

    // Recovery is the end of the first complete scheduled-arrival window with no failed or
    // shed requests and at least the required number of successes. Unobserved recovery is null.
    internal static double? Recovery(IReadOnlyList<RequestMeasurement> requests, double recoveryStart, double recoveryEnd,
        double windowSeconds, int minimumSuccesses)
    {
        var recovered = requests.Where(request => request.ScheduledSeconds >= recoveryStart)
            .OrderBy(request => request.ScheduledSeconds).ToArray();
        if (recovered.Length == 0) { return null; }
        for (var start = recoveryStart; start + windowSeconds <= recoveryEnd; start += windowSeconds)
        {
            var window = recovered.Where(request => request.ScheduledSeconds >= start
                && request.ScheduledSeconds < start + windowSeconds).ToArray();
            if (window.Length >= minimumSuccesses && window.All(request => request.Outcome == "success"))
            {
                return Math.Max(start + windowSeconds, window.Max(request => request.CompletedSeconds)) - recoveryStart;
            }
        }
        return null;
    }

    internal static void UpdatePeak(ref int peak, int current)
    {
        var observed = Volatile.Read(ref peak);
        while (current > observed)
        {
            var previous = Interlocked.CompareExchange(ref peak, current, observed);
            if (previous == observed) { return; }
            observed = previous;
        }
    }
}

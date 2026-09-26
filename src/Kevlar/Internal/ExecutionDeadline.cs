namespace Kevlar.Internal;

// Keep timeout arithmetic monotonic while exposing a stable UTC value for downstream propagation.
internal readonly struct ExecutionDeadline(long startedAt, TimeSpan duration, DateTimeOffset utcDeadline)
{
    internal DateTimeOffset UtcDeadline { get; } = utcDeadline;

    internal TimeSpan Remaining(TimeProvider timeProvider, long timestamp) =>
        duration - timeProvider.GetElapsedTime(startedAt, timestamp);

    internal static ExecutionDeadline Create(TimeProvider timeProvider, long startedAt, TimeSpan duration,
        ExecutionDeadline? parent)
    {
        var utcNow = timeProvider.GetUtcNow();
        var utcTicks = Math.Min(DateTimeOffset.MaxValue.Ticks, utcNow.UtcDateTime.Ticks + duration.Ticks);
        var utcDeadline = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        if (parent is { } outer && outer.UtcDeadline < utcDeadline)
        {
            utcDeadline = outer.UtcDeadline;
        }
        return new ExecutionDeadline(startedAt, duration, utcDeadline);
    }
}

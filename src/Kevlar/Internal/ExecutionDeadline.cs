using System.Runtime.CompilerServices;

namespace Kevlar.Internal;

// Keep timeout arithmetic monotonic. Store UTC ticks so entering a timeout does not
// construct a DateTimeOffset that most executions never read.
internal readonly struct ExecutionDeadline(long startedAt, TimeSpan duration, long utcTicks)
{
    internal bool HasValue => utcTicks != 0;

    internal DateTimeOffset UtcDeadline => new(utcTicks, TimeSpan.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TimeSpan Remaining(TimeProvider timeProvider, long timestamp) =>
        duration - timeProvider.GetElapsedTime(startedAt, timestamp);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ExecutionDeadline Create(TimeProvider timeProvider, long startedAt, TimeSpan duration,
        in ExecutionDeadline parent)
    {
        var nowTicks = ReferenceEquals(timeProvider, TimeProvider.System)
            ? DateTime.UtcNow.Ticks
            : timeProvider.GetUtcNow().UtcDateTime.Ticks;
        var deadlineTicks = Math.Min(DateTimeOffset.MaxValue.Ticks, nowTicks + duration.Ticks);
        if (parent.HasValue && parent.UtcTicks < deadlineTicks)
        {
            deadlineTicks = parent.UtcTicks;
        }
        return new ExecutionDeadline(startedAt, duration, deadlineTicks);
    }

    private long UtcTicks => utcTicks;
}

namespace Kevlar.Internal;

internal static class DelayHelper
{
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    public static TimeSpan Clamp(TimeSpan delay) => delay > MaximumDelay ? MaximumDelay : delay;

    public static TimeSpan FromSecondsClamped(double seconds)
    {
        if (double.IsNaN(seconds) || seconds <= 0)
        {
            return TimeSpan.Zero;
        }

        return seconds >= MaximumDelay.TotalSeconds
            ? MaximumDelay
            : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Waits for <paramref name="delay"/> honouring the context's <see cref="TimeProvider"/> and
    /// cancellation token. In synchronous executions the wait blocks the calling thread so the
    /// pipeline completes synchronously.
    /// </summary>
    public static ValueTask DelayAsync(KevlarContext context, TimeSpan delay)
    {
        var task = CreateDelayTask(context.TimeProvider, delay, context.CancellationToken);

        if (context.IsSynchronous)
        {
            task.GetAwaiter().GetResult();
            return default;
        }

        return new ValueTask(task);
    }

    public static Task CreateDelayTask(TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken)
    {
#if NET
        return Task.Delay(delay, timeProvider, cancellationToken);
#else
        return timeProvider.Delay(delay, cancellationToken);
#endif
    }
}

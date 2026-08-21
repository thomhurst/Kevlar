namespace Kevlar.Chaos.Internal;

internal static class ChaosDelay
{
    public static readonly TimeSpan Maximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    public static ValueTask DelayAsync(KevlarContext context, TimeSpan delay)
    {
        var task = CreateTask(context.TimeProvider, delay, context.CancellationToken);
        if (context.IsSynchronous)
        {
            task.GetAwaiter().GetResult();
            return default;
        }

        return new ValueTask(task);
    }

    private static Task CreateTask(
        TimeProvider timeProvider,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
#if NET
        return Task.Delay(delay, timeProvider, cancellationToken);
#else
        return timeProvider.Delay(delay, cancellationToken);
#endif
    }
}

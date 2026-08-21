namespace Kevlar.Testing;

internal static class SchedulerDrain
{
    public static Task<bool> ObserveAsync(
        Func<bool> condition,
        int maxYields,
        CancellationToken cancellationToken) => Task.Factory.StartNew(
            () =>
            {
                for (var yield = 0; yield < maxYields; yield++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (condition())
                    {
                        return true;
                    }

                    Thread.Yield();
                }

                return condition();
            },
            cancellationToken,
            TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
}

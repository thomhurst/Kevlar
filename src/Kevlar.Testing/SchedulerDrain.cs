namespace Kevlar.Testing;

internal static class SchedulerDrain
{
    public static async Task<bool> ObserveAsync(
        Func<bool> condition,
        int maxYields,
        CancellationToken cancellationToken)
    {
        for (var yield = 0; yield < maxYields; yield++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (condition())
            {
                return true;
            }

            await Task.Yield();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return condition();
    }
}

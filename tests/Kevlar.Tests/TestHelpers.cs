namespace Kevlar.Tests;

internal static class TestHelpers
{
    /// <summary>
    /// Waits for a condition that is satisfied by a thread-pool continuation, bridging the gap
    /// between a FakeTimeProvider.Advance call and the async work it unblocks.
    /// </summary>
    public static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var start = Environment.TickCount64;

        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMilliseconds)
            {
                throw new TimeoutException("The expected condition was not reached in time.");
            }

            await Task.Delay(5);
        }
    }
}

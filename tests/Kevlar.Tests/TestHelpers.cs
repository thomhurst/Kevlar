namespace Kevlar.Tests;

internal static class TestHelpers
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task WaitAsync(Task task, string operation, TimeSpan? timeout = null)
    {
        try
        {
            await task.WaitAsync(timeout ?? DefaultTimeout);
        }
        catch (TimeoutException exception) when (!task.IsCompleted)
        {
            throw new TimeoutException($"Timed out waiting for {operation}.", exception);
        }
    }

    public static async Task<T> WaitAsync<T>(Task<T> task, string operation, TimeSpan? timeout = null)
    {
        try
        {
            return await task.WaitAsync(timeout ?? DefaultTimeout);
        }
        catch (TimeoutException exception) when (!task.IsCompleted)
        {
            throw new TimeoutException($"Timed out waiting for {operation}.", exception);
        }
    }
}

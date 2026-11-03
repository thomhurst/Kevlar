namespace Kevlar.Tests;

internal static class TestHelpers
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task WaitAsync(Task task, string operation, TimeSpan? timeout = null)
    {
        try
        {
            await task.WaitForTestAsync(timeout ?? DefaultTimeout);
        }
        catch (TimeoutException exception) when (!task.IsCompleted)
        {
            throw new TimeoutException($"Timed out waiting for {operation}.", exception);
        }
    }

    /// <summary>
    /// Waits until <paramref name="token"/> is cancelled, then completes <paramref name="cancelled"/>.
    /// </summary>
    /// <remarks>
    /// Do not observe this with a <c>token.Register</c> callback scoped to the awaiting code.
    /// <see cref="CancellationTokenSource.Cancel()"/> runs callbacks newest first, so the delay's
    /// callback completes the await before an earlier observer callback runs. The awaiting code can
    /// then resume on another thread and dispose the observer's registration before it runs, so
    /// a correct cancellation goes unobserved (#686).
    /// </remarks>
    public static async Task DelayUntilCancelledAsync(CancellationToken token, TaskCompletionSource cancelled)
    {
        try
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
        }
        finally
        {
            if (token.IsCancellationRequested)
            {
                cancelled.TrySetResult();
            }
        }
    }

    public static async Task<T> WaitAsync<T>(Task<T> task, string operation, TimeSpan? timeout = null)
    {
        try
        {
            return await task.WaitForTestAsync(timeout ?? DefaultTimeout);
        }
        catch (TimeoutException exception) when (!task.IsCompleted)
        {
            throw new TimeoutException($"Timed out waiting for {operation}.", exception);
        }
    }
}

using TUnit.Core;

namespace Kevlar.Tests;

/// <summary>
/// Bounded waits that also stop when the test runner cancels the current test.
/// </summary>
/// <remarks>
/// A bare <c>task.WaitAsync(timeout)</c> ignores per-test timeouts, session aborts and
/// <c>--fail-fast</c>, so a test whose signal never arrives blocks for its full timeout.
/// These helpers link the timeout with the current test's <see cref="TUnit.Core.Interfaces.ITestExecution.CancellationToken"/>. Outside
/// a test context they behave exactly like the timeout-only overloads.
/// </remarks>
internal static class TestWait
{
    /// <summary>Gets the current test's cancellation token, or <see cref="CancellationToken.None"/> outside a test.</summary>
    public static CancellationToken TestCancellationToken => TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;

    /// <summary>Waits for <paramref name="task"/> until <paramref name="timeout"/> elapses or the current test is cancelled.</summary>
    /// <param name="task">The task to wait for.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes with <paramref name="task"/>, or faults with <see cref="TimeoutException"/> or <see cref="OperationCanceledException"/>.</returns>
    public static Task WaitForTestAsync(this Task task, TimeSpan timeout) =>
        task.WaitAsync(timeout, TestCancellationToken);

    /// <summary>Waits for <paramref name="task"/> until <paramref name="timeout"/> elapses or the current test is cancelled.</summary>
    /// <typeparam name="T">The task's result type.</typeparam>
    /// <param name="task">The task to wait for.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes with the result of <paramref name="task"/>, or faults with <see cref="TimeoutException"/> or <see cref="OperationCanceledException"/>.</returns>
    public static Task<T> WaitForTestAsync<T>(this Task<T> task, TimeSpan timeout) =>
        task.WaitAsync(timeout, TestCancellationToken);
}

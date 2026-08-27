namespace Kevlar.Testing;

/// <summary>Provides bounded, scheduler-driven waits for shield executions under test.</summary>
public static class ShieldExecution
{
    /// <summary>
    /// Waits until caller-observable work has started while verifying that the execution remains pending.
    /// </summary>
    /// <param name="execution">The shield execution expected to remain incomplete.</param>
    /// <param name="workStarted">
    /// A predicate over caller-owned test state that becomes <see langword="true"/> when the expected
    /// work has started.
    /// </param>
    /// <param name="workDescription">A description included in bounded-failure diagnostics.</param>
    /// <param name="maxYields">The maximum number of scheduler yields used to observe the work.</param>
    /// <param name="cancellationToken">Cancels the bounded wait.</param>
    /// <exception cref="ShieldAssertionException">
    /// The execution completes before the work is observed, or the work is not observed within the bound.
    /// </exception>
    public static async ValueTask WaitForPendingAsync(
        Task execution,
        Func<bool> workStarted,
        string workDescription,
        int maxYields = 100,
        CancellationToken cancellationToken = default)
    {
        if (execution is null)
        {
            throw new ArgumentNullException(nameof(execution));
        }

        if (workStarted is null)
        {
            throw new ArgumentNullException(nameof(workStarted));
        }

        if (string.IsNullOrWhiteSpace(workDescription))
        {
            throw new ArgumentException("A work description is required.", nameof(workDescription));
        }

        if (maxYields <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxYields), "MaxYields must be positive.");
        }

        var observed = await SchedulerDrain.ObserveAsync(
            () => execution.IsCompleted || workStarted(),
            maxYields,
            cancellationToken).ConfigureAwait(false);
        if (execution.IsCompleted)
        {
            throw new ShieldAssertionException(
                $"Expected {workDescription} to become pending, but the execution completed " +
                $"with status {execution.Status}.");
        }

        if (observed)
        {
            return;
        }

        throw new ShieldAssertionException(
            $"Expected {workDescription} to become pending within {DescribeYields(maxYields)}, " +
            $"but it was not observed. Execution status: {execution.Status}.");
    }

    private static string DescribeYields(int count) =>
        count == 1 ? "1 scheduler yield" : $"{count} scheduler yields";
}

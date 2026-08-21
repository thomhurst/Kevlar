#if NET8_0_OR_GREATER
using System.Globalization;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Testing;

/// <summary>Provides bounded deterministic advancement for <see cref="FakeTimeProvider"/>.</summary>
public static class FakeTimeProviderExtensions
{
    /// <summary>Advances fake time in fixed steps until a caller-observable condition is satisfied.</summary>
    /// <param name="timeProvider">The fake clock used by the shield.</param>
    /// <param name="step">The positive amount advanced in each step.</param>
    /// <param name="condition">A predicate over caller-owned test state.</param>
    /// <param name="conditionDescription">A description included in bounded-failure diagnostics.</param>
    /// <param name="maxAdvances">The maximum number of time advances.</param>
    /// <param name="maxYieldsPerAdvance">
    /// The maximum scheduler yields allowed for continuations after each advance.
    /// </param>
    /// <param name="cancellationToken">Cancels the bounded advancement.</param>
    /// <exception cref="ShieldAssertionException">The condition is not met within the configured bounds.</exception>
    public static async ValueTask AdvanceUntilAsync(
        this FakeTimeProvider timeProvider,
        TimeSpan step,
        Func<bool> condition,
        string conditionDescription,
        int maxAdvances = 100,
        int maxYieldsPerAdvance = 100,
        CancellationToken cancellationToken = default)
    {
        Validate(timeProvider, step, condition, conditionDescription, maxAdvances, maxYieldsPerAdvance);
        if (condition())
        {
            return;
        }

        for (var advance = 0; advance < maxAdvances; advance++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeProvider.Advance(step);
            if (await SchedulerDrain.ObserveAsync(
                condition,
                maxYieldsPerAdvance,
                cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }

        if (condition())
        {
            return;
        }

        throw new ShieldAssertionException(
            $"Expected {conditionDescription} after {DescribeAdvances(maxAdvances)} of " +
            $"{step.ToString("c", CultureInfo.InvariantCulture)}. Current UTC time: " +
            $"{timeProvider.GetUtcNow():O}.");
    }

    private static void Validate(
        FakeTimeProvider timeProvider,
        TimeSpan step,
        Func<bool> condition,
        string conditionDescription,
        int maxAdvances,
        int maxYieldsPerAdvance)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentException.ThrowIfNullOrWhiteSpace(conditionDescription);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAdvances);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxYieldsPerAdvance);
    }

    private static string DescribeAdvances(int count) =>
        count == 1 ? "1 advance" : $"{count} advances";
}
#endif

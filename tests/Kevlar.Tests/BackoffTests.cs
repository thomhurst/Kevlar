namespace Kevlar.Tests;

public class BackoffTests
{
    private static readonly TimeSpan MaximumRuntimeDelay =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    [Test]
    public async Task None_Returns_Zero_For_Every_Attempt()
    {
        await Assert.That(Backoff.None.GetDelay(1)).IsEqualTo(TimeSpan.Zero);
        await Assert.That(Backoff.None.GetDelay(100)).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Constant_Returns_The_Same_Delay_For_Every_Attempt()
    {
        var backoff = Backoff.Constant(TimeSpan.FromSeconds(3));

        await Assert.That(backoff.GetDelay(1)).IsEqualTo(TimeSpan.FromSeconds(3));
        await Assert.That(backoff.GetDelay(50)).IsEqualTo(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task Linear_Scales_With_The_Attempt_Number()
    {
        var backoff = Backoff.Linear(TimeSpan.FromSeconds(1));

        await Assert.That(backoff.GetDelay(1)).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(backoff.GetDelay(2)).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(backoff.GetDelay(3)).IsEqualTo(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task Linear_Respects_MaxDelay()
    {
        var backoff = Backoff.Linear(TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(2));

        await Assert.That(backoff.GetDelay(1)).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(backoff.GetDelay(2)).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(backoff.GetDelay(10)).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Exponential_Jitter_None_Is_Deterministic()
    {
        var backoff = Backoff.Exponential(
            TimeSpan.FromMilliseconds(250),
            factor: 2,
            jitter: Jitter.None);

        var delays = Enumerable.Range(1, 4).Select(backoff.GetDelay).ToArray();

        await Assert.That(delays).IsEquivalentTo(
            [
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(1000),
                TimeSpan.FromMilliseconds(2000),
            ]);
    }

    [Test]
    public async Task Exponential_Respects_MaxDelay()
    {
        var backoff = Backoff.Exponential(
            TimeSpan.FromSeconds(1),
            factor: 2,
            maxDelay: TimeSpan.FromSeconds(5),
            jitter: Jitter.None);

        await Assert.That(backoff.GetDelay(3)).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(backoff.GetDelay(4)).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(backoff.GetDelay(20)).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Default_Cap_Is_The_Runtime_Timer_Limit_For_BuiltIn_And_Custom_Backoff()
    {
        var exponential = Backoff.Exponential(
            TimeSpan.FromSeconds(1),
            factor: 2,
            jitter: Jitter.None);
        var custom = Backoff.Custom(_ => TimeSpan.MaxValue);

        await Assert.That(exponential.GetDelay(60)).IsEqualTo(MaximumRuntimeDelay);
        await Assert.That(custom.GetDelay(60)).IsEqualTo(MaximumRuntimeDelay);
    }

    [Test]
    public async Task Exponential_Jitter_Equal_Stays_Within_Half_To_OneAndHalf_With_Base_Mean()
    {
        var baseDelay = TimeSpan.FromSeconds(1);
        var backoff = Backoff.Exponential(baseDelay, factor: 1, jitter: Jitter.Equal);
        var totalTicks = 0d;
        var inRange = true;

        for (var sample = 0; sample < 10_000; sample++)
        {
            var delay = backoff.GetDelay(1);
            inRange &= delay >= baseDelay * 0.5 && delay < baseDelay * 1.5;
            totalTicks += delay.Ticks;
        }

        var meanRatio = totalTicks / 10_000 / baseDelay.Ticks;
        await Assert.That(inRange).IsTrue();
        await Assert.That(meanRatio).IsBetween(0.95, 1.05);
    }

    [Test]
    public async Task Exponential_Jitter_Full_Stays_Within_Zero_To_Base()
    {
        var baseDelay = TimeSpan.FromSeconds(1);
        var backoff = Backoff.Exponential(baseDelay, factor: 1, jitter: Jitter.Full);
        var inRange = true;

        for (var sample = 0; sample < 10_000; sample++)
        {
            var delay = backoff.GetDelay(1);
            inRange &= delay >= TimeSpan.Zero && delay < baseDelay;
        }

        await Assert.That(inRange).IsTrue();
    }

    [Test]
    public async Task Exponential_Jitter_Decorrelated_Depends_On_Previous_Delay()
    {
        var initialDelay = TimeSpan.FromMilliseconds(100);
        var maxDelay = TimeSpan.FromSeconds(5);
        var backoff = Backoff.Exponential(
            initialDelay,
            maxDelay: maxDelay,
            jitter: Jitter.Decorrelated);
        var previousDelay = initialDelay;
        var inRange = true;

        for (var attempt = 1; attempt <= 10_000; attempt++)
        {
            var delay = backoff.GetDelay(attempt, previousDelay);
            var upperBound = previousDelay * 3 > maxDelay ? maxDelay : previousDelay * 3;
            inRange &= delay >= initialDelay && delay <= upperBound;
            previousDelay = delay;
        }

        await Assert.That(inRange).IsTrue();
    }

    [Test]
    public async Task Constant_And_Linear_Accept_Equal_And_Full_Jitter()
    {
        var equalConstant = Backoff.Constant(TimeSpan.FromSeconds(1), Jitter.Equal);
        var fullConstant = Backoff.Constant(TimeSpan.FromSeconds(1), Jitter.Full);
        var equalLinear = Backoff.Linear(TimeSpan.FromSeconds(1), jitter: Jitter.Equal);
        var fullLinear = Backoff.Linear(TimeSpan.FromSeconds(1), jitter: Jitter.Full);
        var inRange = true;

        for (var sample = 0; sample < 10_000; sample++)
        {
            var equalConstantDelay = equalConstant.GetDelay(1);
            var fullConstantDelay = fullConstant.GetDelay(1);
            var equalLinearDelay = equalLinear.GetDelay(2);
            var fullLinearDelay = fullLinear.GetDelay(2);
            inRange &= equalConstantDelay >= TimeSpan.FromMilliseconds(500)
                && equalConstantDelay < TimeSpan.FromMilliseconds(1500)
                && fullConstantDelay >= TimeSpan.Zero
                && fullConstantDelay < TimeSpan.FromSeconds(1)
                && equalLinearDelay >= TimeSpan.FromSeconds(1)
                && equalLinearDelay < TimeSpan.FromSeconds(3)
                && fullLinearDelay >= TimeSpan.Zero
                && fullLinearDelay < TimeSpan.FromSeconds(2);
        }

        await Assert.That(inRange).IsTrue();
    }

    [Test]
    public async Task Jitter_Uses_SharedRandom_And_Is_Thread_Safe()
    {
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), factor: 1, jitter: Jitter.Equal);
        var invalidDelays = 0;

        Parallel.For(0, 32, _ =>
        {
            for (var sample = 0; sample < 10_000; sample++)
            {
                var delay = backoff.GetDelay(1);
                if (delay < TimeSpan.FromMilliseconds(500) || delay >= TimeSpan.FromMilliseconds(1500))
                {
                    Interlocked.Increment(ref invalidDelays);
                }
            }
        });

        await Assert.That(invalidDelays).IsEqualTo(0);
    }

    [Test]
    public async Task MaxDelay_Caps_Every_Jitter_Mode_Including_Decorrelated()
    {
        var maxDelay = TimeSpan.FromMilliseconds(10);
        var inRange = true;

        foreach (var jitter in Enum.GetValues<Jitter>())
        {
            var backoff = Backoff.Exponential(
                TimeSpan.FromMilliseconds(5),
                factor: 4,
                maxDelay: maxDelay,
                jitter: jitter);
            var previousDelay = TimeSpan.FromMilliseconds(5);
            for (var attempt = 1; attempt <= 100; attempt++)
            {
                var delay = backoff.GetDelay(attempt, previousDelay);
                inRange &= delay >= TimeSpan.Zero && delay <= maxDelay;
                previousDelay = delay;
            }
        }

        await Assert.That(inRange).IsTrue();
    }

    [Test]
    public async Task Default_Backoff_Is_Capped_At_Thirty_Seconds()
    {
        for (var attempt = 1; attempt <= 64; attempt++)
        {
            var delay = Backoff.Default.GetDelay(attempt);
            await Assert.That(delay >= TimeSpan.Zero).IsTrue();
            await Assert.That(delay <= TimeSpan.FromSeconds(30)).IsTrue();
        }
    }

    [Test]
    public async Task Custom_Backoff_Receives_The_Attempt_Number()
    {
        var seen = new List<int>();
        var backoff = Backoff.Custom(attempt =>
        {
            seen.Add(attempt);
            return TimeSpan.FromMilliseconds(attempt);
        });

        await Assert.That(backoff.GetDelay(1)).IsEqualTo(TimeSpan.FromMilliseconds(1));
        await Assert.That(backoff.GetDelay(7)).IsEqualTo(TimeSpan.FromMilliseconds(7));
        await Assert.That(seen).IsEquivalentTo([1, 7]);
    }

    [Test]
    public async Task Custom_Backoff_Clamps_Negative_Delays_To_Zero()
    {
        var backoff = Backoff.Custom(_ => TimeSpan.FromSeconds(-5));

        await Assert.That(backoff.GetDelay(1)).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Factories_Reject_Invalid_Arguments()
    {
        await Assert.That(() => Backoff.Constant(TimeSpan.FromSeconds(-1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Backoff.Linear(TimeSpan.FromSeconds(-1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Backoff.Exponential(TimeSpan.FromSeconds(-1))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Backoff.Exponential(TimeSpan.FromSeconds(1), factor: 0.5)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Backoff.Constant(TimeSpan.Zero, (Jitter)int.MaxValue)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Backoff.Custom(null!)).Throws<ArgumentNullException>();
    }
}

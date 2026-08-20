namespace Kevlar.Tests;

public class BackoffTests
{
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
    public async Task Exponential_Without_Jitter_Doubles_Each_Attempt()
    {
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), factor: 2.0, jitter: false);

        await Assert.That(backoff.GetDelay(1)).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(backoff.GetDelay(2)).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(backoff.GetDelay(3)).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(backoff.GetDelay(4)).IsEqualTo(TimeSpan.FromSeconds(8));
    }

    [Test]
    public async Task Exponential_Respects_MaxDelay()
    {
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), factor: 2.0, maxDelay: TimeSpan.FromSeconds(5), jitter: false);

        await Assert.That(backoff.GetDelay(3)).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(backoff.GetDelay(4)).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(backoff.GetDelay(20)).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Exponential_Defaults_To_A_One_Day_Ceiling()
    {
        var backoff = Backoff.Exponential(TimeSpan.FromDays(1), factor: 2.0, jitter: false);

        await Assert.That(backoff.GetDelay(10)).IsEqualTo(TimeSpan.FromDays(1));
    }

    [Test]
    public async Task Exponential_Jitter_Stays_Within_Half_To_OneAndAHalf_Times_The_Base()
    {
        var backoff = Backoff.Exponential(TimeSpan.FromSeconds(1), factor: 1.0, jitter: true);

        for (var i = 0; i < 200; i++)
        {
            var delay = backoff.GetDelay(1);
            await Assert.That(delay >= TimeSpan.FromSeconds(0.5)).IsTrue();
            await Assert.That(delay < TimeSpan.FromSeconds(1.5)).IsTrue();
        }
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
        await Assert.That(() => Backoff.Custom(null!)).Throws<ArgumentNullException>();
    }
}

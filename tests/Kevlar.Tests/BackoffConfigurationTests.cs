namespace Kevlar.Tests;

public class BackoffConfigurationTests
{
    [Test]
    public async Task Backoff_Exposes_Configuration()
    {
        var cases = new[]
        {
            new ExpectedConfiguration(Backoff.None, BackoffKind.None, TimeSpan.Zero, null, null, Jitter.None),
            new ExpectedConfiguration(
                Backoff.Constant(TimeSpan.FromMilliseconds(100)),
                BackoffKind.Constant,
                TimeSpan.FromMilliseconds(100),
                null,
                null,
                Jitter.None),
            new ExpectedConfiguration(
                Backoff.Linear(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2)),
                BackoffKind.Linear,
                TimeSpan.FromMilliseconds(200),
                null,
                TimeSpan.FromSeconds(2),
                Jitter.None),
            new ExpectedConfiguration(
                Backoff.Exponential(
                    TimeSpan.FromMilliseconds(250),
                    factor: 3,
                    maxDelay: TimeSpan.FromSeconds(30),
                    jitter: Jitter.None),
                BackoffKind.Exponential,
                TimeSpan.FromMilliseconds(250),
                3,
                TimeSpan.FromSeconds(30),
                Jitter.None),
            new ExpectedConfiguration(Backoff.Custom(_ => TimeSpan.Zero), BackoffKind.Custom, null, null, null, null),
        };

        foreach (var expected in cases)
        {
            await Assert.That(expected.Backoff.Kind).IsEqualTo(expected.Kind);
            await Assert.That(expected.Backoff.BaseDelay).IsEqualTo(expected.BaseDelay);
            await Assert.That(expected.Backoff.Factor).IsEqualTo(expected.Factor);
            await Assert.That(expected.Backoff.MaxDelay).IsEqualTo(expected.MaxDelay);
            await Assert.That(expected.Backoff.Jitter).IsEqualTo(expected.Jitter);
        }
    }

    private sealed record ExpectedConfiguration(
        Backoff Backoff,
        BackoffKind Kind,
        TimeSpan? BaseDelay,
        double? Factor,
        TimeSpan? MaxDelay,
        Jitter? Jitter);
}

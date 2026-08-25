namespace Kevlar.NetStandard.Tests;

public class BackoffTests
{
    [Test]
    public async Task Jitter_Is_Thread_Safe_On_NetStandard_Build()
    {
        var backoff = Backoff.Exponential(
            TimeSpan.FromMilliseconds(1),
            factor: 1,
            jitter: Jitter.Equal);
        var invalidDelays = 0;

        Parallel.For(0, 32, _ =>
        {
            for (var draw = 0; draw < 10_000; draw++)
            {
                var delay = backoff.GetDelay(1);
                if (delay < TimeSpan.FromTicks(5_000) || delay >= TimeSpan.FromTicks(15_000))
                {
                    Interlocked.Increment(ref invalidDelays);
                }
            }
        });

        await Assert.That(invalidDelays).IsEqualTo(0);
    }
}

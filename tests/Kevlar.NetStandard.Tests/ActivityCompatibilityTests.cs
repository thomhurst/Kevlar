using System.Diagnostics;

namespace Kevlar.NetStandard.Tests;

[NotInParallel]
public class ActivityCompatibilityTests
{
    [Test]
    public async Task NetStandard_Asset_Does_Not_Create_Activities()
    {
        var spans = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == KevlarDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _ => Interlocked.Increment(ref spans),
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("application").Start();
        Activity? observed = null;
        var result = await Shield.Retry(1, Backoff.None).ExecuteAsync(_ =>
        {
            observed = Activity.Current;
            return new ValueTask<int>(42);
        });
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(spans).IsEqualTo(0);
        await Assert.That(ReferenceEquals(observed, parent)).IsTrue();
        await Assert.That(KevlarDiagnostics.ActivitySourceName).IsEqualTo("Kevlar");
    }
}

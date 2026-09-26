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

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hedge_Preserves_Parent_And_Awaits_Notification(bool generatedAction)
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
        var notificationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        Activity? observed = null;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.OnHedge = async _ =>
            {
                notificationStarted.TrySetResult();
                await releaseNotification.Task;
            };
            if (generatedAction)
            {
                options.ActionGenerator = _ => _ =>
                {
                    observed = Activity.Current;
                    return new ValueTask<int>(42);
                };
            }
        });

        var execution = shield.ExecuteAsync(_ =>
        {
            observed = Activity.Current;
            return ++attempts == 1
                ? ValueTask.FromException<int>(new InvalidOperationException("primary"))
                : new ValueTask<int>(42);
        }).AsTask();
        try
        {
            await notificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(execution.IsCompleted).IsFalse();
            await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
        }
        finally
        {
            releaseNotification.TrySetResult();
        }

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(generatedAction ? 1 : 2);
        await Assert.That(ReferenceEquals(observed, parent)).IsTrue();
        await Assert.That(spans).IsEqualTo(0);
    }

    [Test]
    public async Task Generated_Hedge_Cancels_Pending_Primary_Without_Creating_Activities()
    {
        using var parent = new Activity("application").Start();
        var primaryStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Activity? observed = null;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = _ => _ =>
            {
                observed = Activity.Current;
                return new ValueTask<int>(42);
            };
        });

        var result = await shield.ExecuteAsync<int>(async cancellationToken =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return -1;
            }
            finally
            {
                primaryStopped.TrySetResult();
            }
        });

        await primaryStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(ReferenceEquals(observed, parent)).IsTrue();
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
    }

    [Test]
    public async Task Exhausted_Hedges_Preserve_Last_Failure()
    {
        var attempts = 0;
        var expected = new InvalidOperationException("last attempt");
        var outcome = await Shield.Hedge(3, delay: Timeout.InfiniteTimeSpan)
            .ExecuteOutcomeAsync<int>(_ =>
            {
                attempts++;
                return ValueTask.FromException<int>(attempts == 4
                    ? expected
                    : new InvalidOperationException("earlier attempt"));
            });

        await Assert.That(attempts).IsEqualTo(4);
        await Assert.That(ReferenceEquals(outcome.Exception, expected)).IsTrue();
    }
}

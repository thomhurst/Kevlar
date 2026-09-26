namespace Kevlar.NetStandard.Tests;

public class AdaptiveConcurrencyCompatibilityTests
{
    [Test]
    public async Task Async_Failure_Shrinks_Admission_And_Releases_Permits_On_NetStandard()
    {
        var time = new ManualTimeProvider();
        var notifications = 0;
        var shield = Shield.ConcurrencyLimit(new AdaptiveConcurrencyLimitOptions
        {
            InitialLimit = 2,
            MaxLimit = 2,
            DecreaseFactor = 0.5,
            OnRejected = async rejected =>
            {
                await Task.Yield();
                await Assert.That(rejected.MaxConcurrency).IsEqualTo(1);
                Interlocked.Increment(ref notifications);
            },
        }).WithTimeProvider(time);
        await Assert.That(shield.Execute(static _ => 42)).IsEqualTo(42);
        time.Advance(TimeSpan.FromSeconds(1));
        var failure = new IOException("downstream");
        var outcome = await shield.ExecuteOutcomeAsync<int>(async _ =>
        {
            await Task.Yield();
            throw failure;
        });
        await Assert.That(ReferenceEquals(outcome.Exception, failure)).IsTrue();

        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = shield.ExecuteAsync(_ => new ValueTask<int>(gate.Task)).AsTask();
        try
        {
            var rejected = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(43));
            await Assert.That(rejected.Exception).IsTypeOf<ConcurrencyLimitExceededException>();
            await Assert.That(notifications).IsEqualTo(1);
        }
        finally
        {
            gate.TrySetResult(42);
            _ = await running;
        }
        await Assert.That(shield.Execute(static _ => 44)).IsEqualTo(44);
    }

    [Test]
    public async Task Caller_Cancellation_Releases_The_Only_Permit_On_NetStandard()
    {
        var shield = Shield.For<int>().ConcurrencyLimit(new AdaptiveConcurrencyLimitOptions
        {
            InitialLimit = 1,
            MaxLimit = 1,
        });
        using var cancellation = new CancellationTokenSource();
        var execution = shield.ExecuteOutcomeAsync(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 42;
        }, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.That((await execution).Exception is OperationCanceledException).IsTrue();
        await Assert.That(shield.Execute(static _ => 43)).IsEqualTo(43);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}

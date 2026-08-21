namespace Kevlar.Tests;

public class CircuitBreakerTimelineTests
{
    [Test]
    public async Task Far_Ahead_Utc_Epoch_Does_Not_Expire_A_Shared_Open_Circuit()
    {
        var firstTime = new ManualTimeProvider(DateTimeOffset.UnixEpoch, 0);
        var secondTime = new ManualTimeProvider(DateTimeOffset.MaxValue.AddDays(-1), long.MaxValue / 2);
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(30)).WithTimeProvider(firstTime);
        var secondCopy = shield.WithTimeProvider(secondTime);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        var rejection = await Assert.That(async () =>
                await secondCopy.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        await Assert.That(rejection!.RetryAfter >= TimeSpan.Zero).IsTrue();
        await Assert.That(rejection.RetryAfter <= TimeSpan.FromSeconds(30)).IsTrue();
    }

    [Test]
    public async Task Far_Behind_Utc_Epoch_Does_Not_Extend_A_Shared_Open_Circuit()
    {
        var firstTime = new ManualTimeProvider(DateTimeOffset.MaxValue.AddDays(-1), long.MaxValue / 2);
        var secondTime = new ManualTimeProvider(DateTimeOffset.UnixEpoch, 0);
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(30)).WithTimeProvider(firstTime);
        var secondCopy = shield.WithTimeProvider(secondTime);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        var rejection = await Assert.That(async () =>
                await secondCopy.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        await Assert.That(rejection!.RetryAfter >= TimeSpan.Zero).IsTrue();
        await Assert.That(rejection.RetryAfter <= TimeSpan.FromSeconds(30)).IsTrue();
    }

    [Test]
    public async Task Backward_Utc_Movement_Does_Not_Extend_The_Break_Duration()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow, 0);
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(30)).WithTimeProvider(timeProvider);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        timeProvider.SetUtcNow(timeProvider.GetUtcNow() - TimeSpan.FromDays(1));
        timeProvider.AdvanceTimestamp(TimeSpan.FromSeconds(30));

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Alternating_Provider_Epochs_Does_Not_Discard_Ratio_History()
    {
        var monitor = new CircuitBreakerMonitor();
        var firstTime = new ManualTimeProvider(DateTimeOffset.UnixEpoch, 0);
        var secondTime = new ManualTimeProvider(DateTimeOffset.MaxValue.AddDays(-1), long.MaxValue / 2);
        var shield = CreateRatioBreaker(firstTime, monitor);
        var secondCopy = shield.WithTimeProvider(secondTime);

        for (var i = 0; i < 3; i++)
        {
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        }

        await secondCopy.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Alternating_Provider_Epochs_Does_Not_Keep_Expired_Ratio_History()
    {
        var monitor = new CircuitBreakerMonitor();
        var firstTime = new ManualTimeProvider(DateTimeOffset.UnixEpoch, 0);
        var secondTime = new ManualTimeProvider(DateTimeOffset.MaxValue.AddDays(-1), long.MaxValue / 2);
        var shield = CreateRatioBreaker(firstTime, monitor);
        var secondCopy = shield.WithTimeProvider(secondTime);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        for (var i = 0; i < 3; i++)
        {
            await secondCopy.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        }

        firstTime.AdvanceTimestamp(TimeSpan.FromSeconds(10));
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Open_Decision_Uses_One_Time_Sample_And_NonNegative_RetryAfter()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow, 0);
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(30)).WithTimeProvider(timeProvider);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        var timestampReads = timeProvider.TimestampReads;
        timeProvider.AdvanceUtcAfterNextRead(TimeSpan.FromSeconds(31));

        var rejection = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        await Assert.That(rejection!.RetryAfter >= TimeSpan.Zero).IsTrue();
        await Assert.That(timeProvider.TimestampReads).IsEqualTo(timestampReads + 1);
    }

    [Test]
    public async Task NonDivisible_Sampling_Window_Does_Not_Expire_A_Tick_Early()
    {
        var monitor = new CircuitBreakerMonitor();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow, 0);
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 1;
                options.MinimumThroughput = 4;
                options.SamplingWindow = TimeSpan.FromTicks(11);
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(timeProvider);

        for (var i = 0; i < 3; i++)
        {
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        }

        timeProvider.AdvanceTimestamp(TimeSpan.FromTicks(10));
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Minimum_Tick_Sampling_Window_Expires_After_One_Tick()
    {
        var monitor = new CircuitBreakerMonitor();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow, 0);
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 1;
                options.MinimumThroughput = 4;
                options.SamplingWindow = TimeSpan.FromTicks(1);
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(timeProvider);

        for (var i = 0; i < 3; i++)
        {
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        }

        timeProvider.AdvanceTimestamp(TimeSpan.FromTicks(1));
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    private static Shield CreateRatioBreaker(TimeProvider timeProvider, CircuitBreakerMonitor monitor) => Shield
        .CircuitBreaker(options =>
        {
            options.FailureRatio = 1;
            options.MinimumThroughput = 4;
            options.SamplingWindow = TimeSpan.FromSeconds(10);
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
        })
        .WithTimeProvider(timeProvider);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private int _timestampReads;
        private long _utcTicks;
        private long _advanceUtcAfterReadTicks;

        public ManualTimeProvider(DateTimeOffset utcNow, long timestamp)
        {
            _utcTicks = utcNow.UtcTicks;
            _timestamp = timestamp;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public int TimestampReads => Volatile.Read(ref _timestampReads);

        public override DateTimeOffset GetUtcNow()
        {
            var utcTicks = Volatile.Read(ref _utcTicks);
            var advance = Interlocked.Exchange(ref _advanceUtcAfterReadTicks, 0);
            if (advance != 0)
            {
                Interlocked.Add(ref _utcTicks, advance);
            }

            return new DateTimeOffset(utcTicks, TimeSpan.Zero);
        }

        public override long GetTimestamp()
        {
            Interlocked.Increment(ref _timestampReads);
            return Volatile.Read(ref _timestamp);
        }

        public void AdvanceTimestamp(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);

        public void AdvanceUtcAfterNextRead(TimeSpan elapsed) =>
            Volatile.Write(ref _advanceUtcAfterReadTicks, elapsed.Ticks);

        public void SetUtcNow(DateTimeOffset utcNow) => Volatile.Write(ref _utcTicks, utcNow.UtcTicks);
    }
}

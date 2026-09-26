using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

[NotInParallel]
public class PriorityQueueTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Higher_Priority_Evicts_Newest_Lowest_And_Admission_Preserves_Fifo_Ties(bool rateLimit)
    {
        await using var queue = new QueueHarness(rateLimit, capacity: 4);
        var active = queue.Submit(0, priority: null);
        var first = queue.Submit(1, priority: int.MinValue);
        var evicted = queue.Submit(2, priority: int.MinValue);
        var tieFirst = queue.Submit(3, priority: 7);
        var tieSecond = queue.Submit(4, priority: 7);
        var highest = queue.Submit(5, priority: int.MaxValue);
        await Assert.That((await evicted.WaitAsync(TestHelpers.DefaultTimeout)).Exception?.GetType())
            .IsEqualTo(rateLimit ? typeof(RateLimitExceededException) : typeof(ConcurrencyLimitExceededException));
        await Assert.That(queue.Reasons.Single()).IsEqualTo("queue_evicted");
        var snapshot = Priorities(queue.Shield);
        await Assert.That(snapshot[int.MinValue]).IsEqualTo(1);
        await Assert.That(snapshot[7]).IsEqualTo(2);
        await Assert.That(snapshot[int.MaxValue]).IsEqualTo(1);
        foreach (var pair in new[] { (0, 5), (5, 3), (3, 4), (4, 1) })
        {
            await queue.AdmitNext(pair.Item1, pair.Item2);
        }
        queue.Release(1);
        await Task.WhenAll(active, first, tieFirst, tieSecond, highest).WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(string.Join(",", queue.Entered)).IsEqualTo("0,5,3,4,1");
        await Assert.That(Queued(queue.Shield)).IsEqualTo(0);
        await Assert.That(snapshot[7]).IsEqualTo(2); // A captured snapshot does not mutate.
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Equal_Or_Lower_Priority_Cannot_Evict_And_Unset_Priority_Is_Zero(bool rateLimit)
    {
        await using var queue = new QueueHarness(rateLimit, capacity: 2);
        var active = queue.Submit(0, priority: null);
        var first = queue.Submit(1, priority: null);
        var second = queue.Submit(2, priority: 0);
        var rejected = await queue.Submit(3, priority: 0).WaitAsync(TestHelpers.DefaultTimeout);
        var lower = await queue.Submit(4, priority: -1).WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(rejected.Exception).IsNotNull();
        await Assert.That(lower.Exception).IsNotNull();
        await Assert.That(queue.Reasons.All(reason => reason is null)).IsTrue();
        await Assert.That(Priorities(queue.Shield)[0]).IsEqualTo(2);
        await queue.AdmitNext(0, 1);
        await queue.AdmitNext(1, 2);
        queue.Release(2);
        await Task.WhenAll(active, first, second).WaitAsync(TestHelpers.DefaultTimeout);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Cancellation_Removes_Head_Without_Consuming_A_Permit(bool rateLimit)
    {
        await using var queue = new QueueHarness(rateLimit, capacity: 2);
        using var caller = new CancellationTokenSource();
        var active = queue.Submit(0, priority: 0);
        var low = queue.Submit(1, priority: -1);
        var cancelled = queue.Submit(2, priority: 5, caller.Token);
        caller.Cancel();
        var outcome = await cancelled.WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken).IsEqualTo(caller.Token);
        await Assert.That(Queued(queue.Shield)).IsEqualTo(1);
        await Assert.That(queue.Reasons.Count).IsEqualTo(0);
        await queue.AdmitNext(0, 1);
        queue.Release(1);
        await Task.WhenAll(active, low).WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(queue.Entered.Contains(2)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Timeout_Rejects_Without_Invoking_And_Admission_Stops_The_Timer(bool rateLimit)
    {
        await using var queue = new QueueHarness(rateLimit, capacity: 2, timeout: TimeSpan.FromMilliseconds(500));
        var active = queue.Submit(0, priority: 0);
        var expired = queue.Submit(1, priority: 10);
        queue.Time.Advance(TimeSpan.FromMilliseconds(500));
        var outcome = await expired.WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(outcome.Exception?.GetType()).IsEqualTo(rateLimit
            ? typeof(RateLimitExceededException) : typeof(ConcurrencyLimitExceededException));
        await Assert.That(queue.Reasons.Single()).IsEqualTo("queue_timeout");
        await Assert.That(Queued(queue.Shield)).IsEqualTo(0);
        queue.Release(0);
        await active.WaitAsync(TestHelpers.DefaultTimeout);
        queue.Time.Advance(TimeSpan.FromMilliseconds(500));
        var admitted = queue.Submit(2, priority: -10);
        await queue.WaitForEntry(2);
        queue.Time.Advance(TimeSpan.FromSeconds(10));
        await Assert.That(admitted.IsCompleted).IsFalse();
        queue.Release(2);
        await Assert.That((await admitted.WaitAsync(TestHelpers.DefaultTimeout)).Exception).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Priority_Is_Ignored_When_Configuration_Is_Disabled(bool rateLimit)
    {
        await using var queue = new QueueHarness(rateLimit, capacity: 1, enabled: false);
        var active = queue.Submit(0, priority: null);
        var first = queue.Submit(1, priority: -100);
        var rejected = await queue.Submit(2, priority: 100).WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(rejected.Exception).IsNotNull();
        await Assert.That(Priorities(queue.Shield).Count).IsEqualTo(0);
        await queue.AdmitNext(0, 1);
        queue.Release(1);
        await Task.WhenAll(active, first).WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(queue.Shield.ToString()).DoesNotContain("priority");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Synchronous_Execution_Joins_The_Same_Priority_Queue(bool rateLimit)
    {
        await using var queue = new QueueHarness(rateLimit, capacity: 2);
        var active = queue.Submit(0, priority: 0);
        var low = queue.Submit(1, priority: -1);
        var synchronous = Task.Factory.StartNew(() => queue.Shield.ExecuteWithContext(
            5, static (priority, properties) => properties.Set(KevlarKeys.Priority, priority),
            static (_, _) => 42), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        using var timeout = new CancellationTokenSource(TestHelpers.DefaultTimeout);
        while (Queued(queue.Shield) != 2)
        {
            await Task.Delay(1, timeout.Token);
        }
        queue.Release(0);
        queue.Time.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await synchronous.WaitAsync(TestHelpers.DefaultTimeout)).IsEqualTo(42);
        queue.Time.Advance(TimeSpan.FromSeconds(1));
        await queue.WaitForEntry(1);
        queue.Release(1);
        await Task.WhenAll(active, low).WaitAsync(TestHelpers.DefaultTimeout);
    }

    [Test]
    public async Task Configuration_And_Descriptors_Expose_Priority_Mode_For_Typed_And_Untyped_Shields()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConcurrencyLimit:QueueLimit"] = "2",
            ["ConcurrencyLimit:UsePriorityQueue"] = "true",
            ["RateLimit:QueueLimit"] = "3",
            ["RateLimit:UsePriorityQueue"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddShield("priority", configuration);
        services.AddShield<int>("typed-priority", configuration);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();
        foreach (var descriptor in new[] { registry.GetShield("priority").GetDescriptor(), registry.GetShield<int>("typed-priority").GetDescriptor() })
        {
            await Assert.That(descriptor.AssertContainsSingle<ConcurrencyLimitStrategyDescriptor>().UsePriorityQueue).IsTrue();
            await Assert.That(descriptor.AssertContainsSingle<RateLimitStrategyDescriptor>().UsePriorityQueue).IsTrue();
            await Assert.That(descriptor.AssertContainsSingle<ConcurrencyLimitStrategyDescriptor>().Description).Contains("priority queue");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Eviction_Reports_Reason_Without_A_Priority_Metric_Tag(bool rateLimit)
    {
        using var meter = new MeterListener();
        var measurements = new ConcurrentQueue<KeyValuePair<string, object?>[]>();
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == KevlarDiagnostics.MeterName && instrument.Name == "kevlar.rejections")
                listener.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.Enqueue(tags.ToArray()));
        meter.Start();
        var telemetry = new RejectionListener();
        using var subscription = KevlarDiagnostics.Listen(telemetry);
        await using var queue = new QueueHarness(rateLimit, capacity: 1);
        _ = queue.Submit(0, priority: 0);
        var evicted = queue.Submit(1, priority: -10);
        _ = queue.Submit(2, priority: 10);
        await evicted.WaitAsync(TestHelpers.DefaultTimeout);
        var tags = measurements.Single();
        await Assert.That(tags.Single(tag => tag.Key == "kevlar.rejection.reason").Value).IsEqualTo("queue_evicted");
        await Assert.That(tags.Any(tag => tag.Key.Contains("priority"))).IsFalse();
        await Assert.That(telemetry.Reasons.Single()).IsEqualTo("queue_evicted");
    }

    [Test]
    public async Task Concurrent_Admission_And_Cancellation_Preserve_The_Concurrency_Bound()
    {
        const int limit = 3;
        var running = 0;
        var violations = 0;
        var shield = Shield.ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = limit;
            options.QueueLimit = 4;
            options.UsePriorityQueue = true;
        });
        var workers = Enumerable.Range(0, 12).Select(async worker =>
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                using var caller = new CancellationTokenSource();
                var execution = shield.ExecuteWithContextAsync(worker,
                    static (priority, properties) => properties.Set(KevlarKeys.Priority, priority),
                    async (_, _) =>
                    {
                        if (Interlocked.Increment(ref running) > limit) Interlocked.Increment(ref violations);
                        try { await Task.Yield(); return 1; }
                        finally { Interlocked.Decrement(ref running); }
                    }, caller.Token).AsTask();
                if (attempt % 3 == 0) caller.Cancel();
                try { await execution; }
                catch (OperationCanceledException) { }
                catch (ConcurrencyLimitExceededException) { }
            }
        });
        await Task.WhenAll(workers).WaitAsync(TestHelpers.DefaultTimeout);
        var state = (ConcurrencyLimitStateSnapshot)shield.GetStateSnapshot().Strategies.Single();
        await Assert.That(violations).IsEqualTo(0);
        await Assert.That(state.AvailablePermits).IsEqualTo(limit);
        await Assert.That(state.RunningExecutions).IsEqualTo(0);
        await Assert.That(state.QueuedExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task Rate_Admission_Rechecks_Time_After_Installing_The_Relative_Timer()
    {
        var time = new AdvancingTimerProvider();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromSeconds(1);
            options.QueueLimit = 1;
            options.UsePriorityQueue = true;
        }).WithTimeProvider(time);
        await shield.ExecuteAsync(static _ => new ValueTask<int>(1));
        var queued = shield.ExecuteAsync(static _ => new ValueTask<int>(2)).AsTask();
        await Assert.That(await queued.WaitAsync(TestHelpers.DefaultTimeout)).IsEqualTo(2);
        await Assert.That(Queued(shield)).IsEqualTo(0);
    }

    private sealed class AdvancingTimerProvider : TimeProvider
    {
        private readonly FakeTimeProvider _time = new();
        private int _created;
        public override long TimestampFrequency => _time.TimestampFrequency;
        public override long GetTimestamp() => _time.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => _time.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Increment(ref _created) == 1) _time.Advance(dueTime);
            return _time.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class RejectionListener : IKevlarTelemetryListener
    {
        public ConcurrentQueue<string?> Reasons { get; } = new();
        public void OnEvent(in KevlarTelemetryEvent telemetryEvent)
        {
            if (telemetryEvent.RejectionReason is { } reason) Reasons.Enqueue(reason);
        }
    }

    private static int Queued(Shield shield) => shield.GetStateSnapshot().Strategies.Single() switch
    {
        ConcurrencyLimitStateSnapshot concurrency => concurrency.QueuedExecutions,
        RateLimitStateSnapshot rate => rate.QueuedExecutions,
        _ => throw new InvalidOperationException(),
    };

    private static IReadOnlyDictionary<int, int> Priorities(Shield shield) => shield.GetStateSnapshot().Strategies.Single() switch
    {
        ConcurrencyLimitStateSnapshot concurrency => concurrency.QueuedByPriority,
        RateLimitStateSnapshot rate => rate.QueuedByPriority,
        _ => throw new InvalidOperationException(),
    };

    private sealed class QueueHarness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cleanup = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<int>> _releases = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<int>> _entries = new();
        private readonly List<Task<Outcome<int>>> _tasks = new();
        public FakeTimeProvider Time { get; } = new();
        public Shield Shield { get; }
        public ConcurrentQueue<int> Entered { get; } = new();
        public ConcurrentQueue<string?> Reasons { get; } = new();

        public QueueHarness(bool rateLimit, int capacity, bool enabled = true, TimeSpan? timeout = null)
        {
            Shield = (rateLimit ? Kevlar.Shield.RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(1);
                options.QueueLimit = capacity;
                options.QueueTimeout = timeout;
                options.UsePriorityQueue = enabled;
                options.OnRejected = rejection => { Reasons.Enqueue(rejection.Reason); return default; };
            }) : Kevlar.Shield.ConcurrencyLimit(options =>
            {
                options.MaxConcurrency = 1;
                options.QueueLimit = capacity;
                options.QueueTimeout = timeout;
                options.UsePriorityQueue = enabled;
                options.OnRejected = rejection => { Reasons.Enqueue(rejection.Reason); return default; };
            })).WithTimeProvider(Time);
        }

        public Task<Outcome<int>> Submit(int id, int? priority, CancellationToken cancellationToken = default)
        {
            _releases[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var execution = RunAsync(id, priority, cancellationToken);
            _tasks.Add(execution);
            return execution;
        }

        private async Task<Outcome<int>> RunAsync(int id, int? priority, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cleanup.Token, cancellationToken);
            try
            {
                var result = await Shield.ExecuteWithContextAsync(priority,
                    static (value, properties) => { if (value is { } p) properties.Set(KevlarKeys.Priority, p); },
                    (_, _) =>
                    {
                        Entered.Enqueue(id);
                        _entries.GetOrAdd(id, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(id);
                        return new ValueTask<int>(_releases[id].Task);
                    }, cancellationToken.CanBeCanceled ? cancellationToken : linked.Token);
                return Outcome<int>.FromResult(result);
            }
            catch (Exception exception)
            {
                return Outcome<int>.FromException(exception);
            }
        }

        public Task WaitForEntry(int id) => _entries.GetOrAdd(id,
            static _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TestHelpers.DefaultTimeout);
        public void Release(int id) => _releases[id].TrySetResult(id);
        public async Task AdmitNext(int previous, int next)
        {
            Release(previous);
            Time.Advance(TimeSpan.FromSeconds(1));
            await WaitForEntry(next);
        }

        public async ValueTask DisposeAsync()
        {
            _cleanup.Cancel();
            foreach (var release in _releases.Values) release.TrySetResult(0);
            await Task.WhenAll(_tasks).WaitAsync(TestHelpers.DefaultTimeout);
            _cleanup.Dispose();
        }
    }
}

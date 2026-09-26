using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CircuitBreakerProbeAndSlowCallTests
{
    [Test]
    [Arguments("fixed")]
    [Arguments("callback")]
    [Arguments("generator")]
    public async Task HalfOpen_Admits_One_Bounded_Cohort_And_Waits_For_Every_Probe(string mode)
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.HalfOpenProbes = 3;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
            if (mode == "callback")
            {
                options.OnStateChanged = static _ => default;
            }
            if (mode == "generator")
            {
                options.BreakDurationGenerator = static _ => new(TimeSpan.FromSeconds(1));
            }
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        var releases = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var probes = releases.Select(release => shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(release.Task)).AsTask()).ToArray();
        try
        {
            await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(3);
            await Assert.That((await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42))).Exception).IsTypeOf<CircuitOpenException>();
            releases[0].SetResult(42);
            _ = await probes[0];
            await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
            await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(2);
            await Assert.That((await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42))).Exception).IsTypeOf<CircuitOpenException>();
            releases[1].SetResult(42);
            releases[2].SetResult(42);
            _ = await Task.WhenAll(probes);
            await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
            await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(0);
        }
        finally
        {
            foreach (var release in releases)
            {
                release.TrySetResult(42);
            }
            _ = await Task.WhenAll(probes);
        }
    }

    [Test]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    public async Task Probe_Failures_Respect_The_Trip_Mode_And_Ignore_Late_Outcomes(int failures, bool ratioMode)
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = ratioMode ? 0.5 : null;
            options.ConsecutiveFailures = ratioMode ? null : 1;
            options.MinimumThroughput = 1;
            options.HalfOpenProbes = 3;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        var releases = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var probes = releases.Select(release => shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(release.Task)).AsTask()).ToArray();
        try
        {
            for (var index = 0; index < failures; index++)
            {
                releases[index].SetException(new IOException());
                _ = await probes[index];
            }
            await Assert.That(monitor.State).IsEqualTo(ratioMode && failures == 1 ? CircuitState.HalfOpen : CircuitState.Open);
            foreach (var release in releases)
            {
                release.TrySetResult(42);
            }
            _ = await Task.WhenAll(probes);
            await Assert.That(monitor.State).IsEqualTo(ratioMode && failures == 1 ? CircuitState.Closed : CircuitState.Open);
        }
        finally
        {
            foreach (var release in releases)
            {
                release.TrySetResult(42);
            }
            _ = await Task.WhenAll(probes);
        }
    }

    [Test]
    public async Task Unhandled_Probe_Releases_Its_Slot_Without_Counting_As_Completed()
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.HalfOpenProbes = 2;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.HandlesException = static exception => exception is IOException;
            options.Monitor = monitor;
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new InvalidOperationException()));
        await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(0);
        _ = shield.Execute(static _ => 42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
        _ = shield.Execute(static _ => 42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Slow_Successes_Open_The_Ratio_Breaker_Without_Changing_Results(bool dynamicDuration)
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.For<int>().CircuitBreaker(options =>
        {
            options.FailureRatio = 1;
            options.MinimumThroughput = 2;
            options.SamplingWindow = TimeSpan.FromSeconds(10);
            options.SlowCallThreshold = TimeSpan.FromMilliseconds(10);
            options.SlowCallRatio = 0.5;
            options.Monitor = monitor;
            if (dynamicDuration)
            {
                options.BreakDurationGenerator = static _ => new(TimeSpan.FromMinutes(1));
            }
        }).WithTimeProvider(time);
        var first = shield.Execute(_ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(11));
            return 42;
        });
        await Assert.That(first).IsEqualTo(42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
        // A fast success reaches MinimumThroughput while the slow ratio remains at its threshold.
        await Assert.That(shield.Execute(static _ => 43)).IsEqualTo(43);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
        var snapshot = shield.GetStateSnapshot().Strategies.OfType<CircuitBreakerStateSnapshot>().Single();
        await Assert.That(snapshot.SlowCallCount).IsEqualTo(1L);
        time.Advance(TimeSpan.FromSeconds(10));
        await Assert.That(shield.GetStateSnapshot().Strategies.OfType<CircuitBreakerStateSnapshot>().Single().SlowCallCount).IsEqualTo(0L);
    }

    [Test]
    public async Task Threshold_Equality_Is_Fast_And_Description_Includes_Only_Opt_In_Settings()
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 1;
            options.MinimumThroughput = 1;
            options.HalfOpenProbes = 2;
            options.SlowCallThreshold = TimeSpan.FromMilliseconds(10);
            options.SlowCallRatio = 0.5;
            options.Monitor = monitor;
        }).WithTimeProvider(time);
        _ = shield.Execute(_ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(10));
            return 42;
        });
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
        await Assert.That(Snapshot(shield).SlowCallCount).IsEqualTo(0L);
        await Assert.That(shield.ToString()).Contains("probes 2");
        await Assert.That(shield.ToString()).Contains("slow >10ms ratio 50%");
        await Assert.That(Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: TimeSpan.FromSeconds(1)).ToString())
            .IsEqualTo("CircuitBreaker(1 consecutive, break 1s)");
    }

    [Test]
    public async Task Concurrent_Admissions_Do_Not_Exceed_The_Probe_Limit()
    {
        var time = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.HalfOpenProbes = 3;
            options.BreakDuration = TimeSpan.FromSeconds(1);
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = 0;
        var attempted = 0;
        var executions = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            var execution = shield.ExecuteOutcomeAsync(_ =>
            {
                Interlocked.Increment(ref admitted);
                return new ValueTask<int>(release.Task);
            });
            Interlocked.Increment(ref attempted);
            return await execution;
        })).ToArray();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref attempted) != 50)
            {
                await Task.Delay(1, timeout.Token);
            }
            await Assert.That(admitted).IsEqualTo(3);
            await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(3);
            release.SetResult(42);
            var outcomes = await Task.WhenAll(executions);
            await Assert.That(outcomes.Count(outcome => outcome.IsSuccess)).IsEqualTo(3);
            await Assert.That(outcomes.Count(outcome => outcome.Exception is CircuitOpenException)).IsEqualTo(47);
        }
        finally
        {
            release.TrySetResult(42);
            _ = await Task.WhenAll(executions);
        }
    }

    [Test]
    public async Task Failed_Duration_Generator_Preserves_Other_InFlight_Probe_Slots()
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var generations = 0;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.HalfOpenProbes = 2;
            options.Monitor = monitor;
            options.BreakDurationGenerator = _ => ++generations == 2
                ? throw new InvalidOperationException("duration failed")
                : new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        try
        {
            var failed = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
            await Assert.That(failed.Exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
            await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(1);
            _ = shield.Execute(static _ => 42);
            await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
            release.SetResult(42);
            _ = await pending;
            await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
        }
        finally
        {
            release.TrySetResult(42);
            _ = await pending;
        }
    }

    [Test]
    public async Task Slow_And_Failed_Call_Is_Counted_Once_In_The_Window()
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 1;
            options.MinimumThroughput = 3;
            options.SlowCallThreshold = TimeSpan.FromMilliseconds(1);
            options.SlowCallRatio = 0.5;
            options.Monitor = monitor;
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(2));
            return ValueTask.FromException<int>(new IOException());
        });
        _ = shield.Execute(static _ => 42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
        _ = shield.Execute(static _ => 42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
        await Assert.That(Snapshot(shield).SlowCallCount).IsEqualTo(1L);
    }

    [Test]
    public async Task Slow_HalfOpen_Probes_Reopen_Without_Changing_Successful_Results()
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 1;
            options.MinimumThroughput = 1;
            options.HalfOpenProbes = 2;
            options.SlowCallThreshold = TimeSpan.FromMilliseconds(1);
            options.SlowCallRatio = 0.5;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        }).WithTimeProvider(time);
        _ = shield.Execute(_ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(2));
            return 42;
        });
        time.Advance(TimeSpan.FromSeconds(1));
        var result = await shield.ExecuteAsync(async _ =>
        {
            await Task.Yield();
            time.Advance(TimeSpan.FromMilliseconds(2));
            return 43;
        });
        await Assert.That(result).IsEqualTo(43);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Cancelled_Probe_Allows_A_Replacement()
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.HalfOpenProbes = 2;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        var cancelledProbe = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        }, cancellation.Token).AsTask();

        await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(1);
        cancellation.Cancel();
        var outcome = await cancelledProbe;
        await Assert.That(outcome.Exception is OperationCanceledException).IsTrue();
        await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(0);
        _ = shield.Execute(static _ => 42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
        _ = shield.Execute(static _ => 42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Reset_Prevents_An_Old_Probe_From_Changing_A_New_Cohort()
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.HalfOpenProbes = 2;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        var oldRelease = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentRelease = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldProbe = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(oldRelease.Task)).AsTask();
        Task<Outcome<int>>? currentProbe = null;
        try
        {
            monitor.Reset();
            _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
            time.Advance(TimeSpan.FromSeconds(1));
            currentProbe = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(currentRelease.Task)).AsTask();
            oldRelease.SetException(new IOException("stale probe"));
            _ = await oldProbe;
            await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(1);
            await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
            _ = shield.Execute(static _ => 42);
            await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
            currentRelease.SetResult(42);
            _ = await currentProbe;
            await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
        }
        finally
        {
            oldRelease.TrySetResult(42);
            currentRelease.TrySetResult(42);
            _ = await oldProbe;
            if (currentProbe is not null)
            {
                _ = await currentProbe;
            }
        }
    }

    [Test]
    public async Task Pending_Duration_Generator_Prevents_Other_Probes_From_Closing_The_Circuit()
    {
        var time = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var duration = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var generationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generations = 0;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.HalfOpenProbes = 3;
            options.Monitor = monitor;
            options.BreakDurationGenerator = _ =>
            {
                if (++generations == 1)
                {
                    return new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
                }
                generationStarted.TrySetResult();
                return new ValueTask<TimeSpan>(duration.Task);
            };
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        var second = shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        var failed = shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException())).AsTask();
        try
        {
            await generationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult(42);
            _ = await Task.WhenAll(first, second);
            await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
            await Assert.That(Snapshot(shield).ProbesInFlight).IsEqualTo(0);
            var rejected = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
            await Assert.That(rejected.Exception).IsTypeOf<CircuitOpenException>();
            duration.SetResult(TimeSpan.FromSeconds(1));
            _ = await failed;
            await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
        }
        finally
        {
            release.TrySetResult(42);
            duration.TrySetResult(TimeSpan.FromSeconds(1));
            _ = await Task.WhenAll(first, second, failed);
        }
    }

    [Test]
    public async Task Probe_Duration_Generator_Distinguishes_Total_And_Consecutive_Failures()
    {
        var time = new FakeTimeProvider();
        long failures = 0;
        var consecutiveFailures = 0;
        double failureRate = 0;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 0.5;
            options.MinimumThroughput = 1;
            options.HalfOpenProbes = 4;
            options.BreakDurationGenerator = trip =>
            {
                failures = trip.FailureCount;
                consecutiveFailures = trip.ConsecutiveFailures;
                failureRate = trip.FailureRate;
                return new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
            };
        }).WithTimeProvider(time);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(1));
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        _ = shield.Execute(static _ => 42);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));

        await Assert.That(failures).IsEqualTo(2L);
        await Assert.That(consecutiveFailures).IsEqualTo(1);
        await Assert.That(failureRate).IsEqualTo(0.5);
    }

    private static CircuitBreakerStateSnapshot Snapshot(Shield shield) =>
        shield.GetStateSnapshot().Strategies.OfType<CircuitBreakerStateSnapshot>().Single();
}

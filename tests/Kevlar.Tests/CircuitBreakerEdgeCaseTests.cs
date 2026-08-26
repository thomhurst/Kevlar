using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CircuitBreakerEdgeCaseTests
{
    [Test]
    public async Task A_Success_Resets_The_Consecutive_Failure_Count()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 3;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
        });

        for (var round = 0; round < 3; round++)
        {
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
            await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        }

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        // Without the interleaved successes, three failures in a row now trip it.
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Rejections_Carry_RetryAfter_And_The_Last_Failure()
    {
        var fakeTime = new FakeTimeProvider();
        var rootCause = new InvalidOperationException("root cause");
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(30)).WithTimeProvider(fakeTime);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw rootCause))
            .Throws<InvalidOperationException>();

        var rejection = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        await Assert.That(rejection!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(rejection.IsIsolated).IsFalse();
        await Assert.That(ReferenceEquals(rejection.InnerException, rootCause)).IsTrue();

        fakeTime.Advance(TimeSpan.FromSeconds(10));

        var laterRejection = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        await Assert.That(laterRejection!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(20));
    }

    [Test]
    public async Task Isolated_Rejections_Are_Flagged_As_Isolated()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 5;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
        });

        monitor.Isolate();

        var rejection = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        await Assert.That(rejection!.IsIsolated).IsTrue();
        await Assert.That(rejection.RetryAfter).IsNull();
    }

    [Test]
    public async Task The_Circuit_Stays_Open_Until_The_Break_Duration_Fully_Elapses()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(30)).WithTimeProvider(fakeTime);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        fakeTime.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromMilliseconds(1));

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(7));
        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task Only_One_Probe_Is_Allowed_While_HalfOpen()
    {
        var fakeTime = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDuration = TimeSpan.FromSeconds(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(fakeTime);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        fakeTime.Advance(TimeSpan.FromSeconds(1));

        var gate = new TaskCompletionSource();
        var probeStarted = new TaskCompletionSource();

        var probe = shield.ExecuteAsync(async _ =>
        {
            probeStarted.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await probeStarted.Task;
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);

        // A concurrent execution during the probe is rejected, not run.
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(2)))
            .Throws<CircuitOpenException>();

        gate.SetResult();
        await Assert.That(await probe).IsEqualTo(1);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task A_Cancelled_Probe_Frees_The_Slot_For_The_Next_Probe()
    {
        var fakeTime = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        using var cancellation = new CancellationTokenSource();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDuration = TimeSpan.FromSeconds(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(fakeTime);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        fakeTime.Advance(TimeSpan.FromSeconds(1));

        var probeStarted = new TaskCompletionSource();
        var probe = shield.ExecuteAsync(async token =>
        {
            probeStarted.SetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();

        await probeStarted.Task;
        cancellation.Cancel();

        await Assert.That(async () => await probe).Throws<OperationCanceledException>();

        // The cancelled probe said nothing about downstream health: still half-open,
        // and the probe slot is free for the next execution, which closes the circuit.
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(9));
        await Assert.That(result).IsEqualTo(9);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Unhandled_Exception_Does_Not_Reset_Consecutive_Failures()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .When<InvalidOperationException>()
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 3;
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            });

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new ArgumentException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Sync_Unhandled_Exception_Does_Not_Reset_Consecutive_Failures()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .When<InvalidOperationException>()
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 2;
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            });

        await Assert.That(() => shield.Execute<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(() => shield.Execute<int>(_ => throw new ArgumentException()))
            .Throws<ArgumentException>();
        await Assert.That(() => shield.Execute<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Async_Configured_Unhandled_Exception_Does_Not_Reset_Failures()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .When<InvalidOperationException>()
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 2;
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
                options.OnStateChanged = static _ => default;
            });

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new ArgumentException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Unhandled_Exception_Does_Not_Count_Toward_Ratio_Throughput()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .When<InvalidOperationException>()
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 0.8;
                options.MinimumThroughput = 3;
                options.SamplingWindow = TimeSpan.FromMinutes(1);
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            });

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new ArgumentException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Unhandled_HalfOpen_Exception_Releases_The_Probe_Slot()
    {
        var fakeTime = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .When<InvalidOperationException>()
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDuration = TimeSpan.FromSeconds(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(fakeTime);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        await shield.ExecuteOutcomeAsync<int>(_ => throw new ArgumentException());
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Ratio_Mode_Needs_Minimum_Throughput_Before_Tripping()
    {
        var fakeTime = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 1.0;
                options.MinimumThroughput = 4;
                options.SamplingWindow = TimeSpan.FromSeconds(10);
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(fakeTime);

        for (var i = 0; i < 3; i++)
        {
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        }

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Ratio_Mode_Forgets_Failures_Outside_The_Sampling_Window()
    {
        var fakeTime = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 0.5;
                options.MinimumThroughput = 4;
                options.SamplingWindow = TimeSpan.FromSeconds(10);
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(fakeTime);

        for (var i = 0; i < 3; i++)
        {
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        }

        // Let the whole sampling window pass; the three failures above expire.
        fakeTime.Advance(TimeSpan.FromSeconds(60));

        // If the old failures still counted, this fourth failure would reach the minimum
        // throughput with a 100% failure rate and trip the circuit.
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Ratio_Mode_Counts_Successes_In_The_Ratio()
    {
        var fakeTime = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 0.75;
                options.MinimumThroughput = 4;
                options.SamplingWindow = TimeSpan.FromSeconds(10);
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(fakeTime);

        // 2 failures + 2 successes = 50% failure rate: below the 75% threshold.
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        // Four more failures push the rate to 6/8 = 75%: trips.
        for (var i = 0; i < 4; i++)
        {
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        }

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Reset_Closes_The_Circuit_And_Clears_Failure_History()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 2;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
        });

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);

        monitor.Reset();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        // History was cleared: one failure is not enough to trip again.
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(5));
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task Monitor_StateChanged_Event_Sees_Every_Transition()
    {
        var fakeTime = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var transitions = new List<(CircuitState From, CircuitState To)>();
        monitor.StateChanged += change => transitions.Add((change.From, change.To));

        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDuration = TimeSpan.FromSeconds(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(fakeTime);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(transitions).IsEquivalentTo(
        [
            (CircuitState.Closed, CircuitState.Open),
            (CircuitState.Open, CircuitState.HalfOpen),
            (CircuitState.HalfOpen, CircuitState.Closed),
        ]);
    }

    [Test]
    public async Task Transition_Events_Carry_The_Causing_Exception()
    {
        var cause = new InvalidOperationException("cause");
        CircuitBreakerStateChangedEvent? opened = null;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.OnStateChanged = change =>
            {
                opened ??= change;
                return default;
            };
        });

        await shield.ExecuteOutcomeAsync<int>(_ => throw cause);

        await Assert.That(opened).IsNotNull();
        await Assert.That(opened!.Value.To).IsEqualTo(CircuitState.Open);
        await Assert.That(ReferenceEquals(opened.Value.LastException, cause)).IsTrue();
    }

    [Test]
    public async Task Result_Handling_Clauses_Can_Trip_The_Circuit()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.For<int>()
            .WhenResult(value => value < 0)
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 2;
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.Monitor = monitor;
            });

        await shield.ExecuteAsync(_ => new ValueTask<int>(-1));
        await shield.ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
    }
}

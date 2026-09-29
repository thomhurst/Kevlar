using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

/// <summary>
/// Rejections from Open, Isolated, and saturated HalfOpen circuits skip the breaker gate.
/// These tests hold them to the locked path's contract under concurrent callers and transitions.
/// </summary>
public class CircuitBreakerLockFreeRejectionTests
{
    private const int Workers = 8;
    private const int CallsPerWorker = 2_000;

    [Test]
    public async Task Concurrent_Open_Rejections_Report_The_Opening_Failure_And_A_Bounded_RetryAfter()
    {
        var breakDuration = TimeSpan.FromHours(1);
        var failure = new InvalidOperationException("downstream outage");
        var shield = Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: breakDuration);
        await shield.ExecuteOutcomeAsync<int>(_ => throw failure);

        var invoked = 0;
        var invalid = 0;
        await Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
        {
            for (var call = 0; call < CallsPerWorker; call++)
            {
                // Rejections complete synchronously; yield so the storm cannot starve the pool.
                await Task.Yield();
                var outcome = await shield.ExecuteOutcomeAsync(_ =>
                {
                    Interlocked.Increment(ref invoked);
                    return new ValueTask<int>(1);
                });
                if (outcome.Exception is not CircuitOpenException
                    {
                        IsIsolated: false,
                        RetryAfter: { } retryAfter,
                    } rejection
                    || !ReferenceEquals(rejection.InnerException, failure)
                    || retryAfter <= TimeSpan.Zero
                    || retryAfter > breakDuration)
                {
                    Interlocked.Increment(ref invalid);
                }
            }
        })));

        await Assert.That(Volatile.Read(ref invoked)).IsEqualTo(0);
        await Assert.That(Volatile.Read(ref invalid)).IsEqualTo(0);
    }

    [Test]
    public async Task Open_RetryAfter_Counts_Down_With_The_System_Clock()
    {
        var breakDuration = TimeSpan.FromHours(1);
        var shield = Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: breakDuration);
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        var first = await RejectAsync(shield);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        var second = await RejectAsync(shield);

        await Assert.That(first.RetryAfter!.Value <= breakDuration).IsTrue();
        await Assert.That(first.RetryAfter.Value - second.RetryAfter!.Value >= TimeSpan.FromMilliseconds(90))
            .IsTrue();
    }

    [Test]
    public async Task Concurrent_Isolated_Rejections_Report_Isolation_Without_RetryAfter()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options => options.Monitor = monitor)
            .WithTimeProvider(new FakeTimeProvider());
        await monitor.IsolateAsync();

        var invalid = 0;
        await Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
        {
            for (var call = 0; call < CallsPerWorker; call++)
            {
                // Rejections complete synchronously; yield so the storm cannot starve the pool.
                await Task.Yield();
                var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
                if (outcome.Exception is not CircuitOpenException { IsIsolated: true, RetryAfter: null })
                {
                    Interlocked.Increment(ref invalid);
                }
            }
        })));

        await Assert.That(Volatile.Read(ref invalid)).IsEqualTo(0);
    }

    [Test]
    public async Task Isolate_Preserves_The_Opening_Failure_For_Lock_Free_Rejections()
    {
        var monitor = new CircuitBreakerMonitor();
        var failure = new InvalidOperationException("downstream outage");
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromHours(1);
            options.Monitor = monitor;
        });
        await shield.ExecuteOutcomeAsync<int>(_ => throw failure);
        await RejectAsync(shield);

        await monitor.IsolateAsync();
        var rejection = await RejectAsync(shield);

        await Assert.That(rejection.IsIsolated).IsTrue();
        await Assert.That(rejection.RetryAfter).IsNull();
        await Assert.That(rejection.InnerException).IsSameReferenceAs(failure);
    }

    [Test]
    public async Task Manual_Transitions_During_Rejection_Storm_Are_Observed_By_Later_Calls()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromHours(1);
            options.Monitor = monitor;
        });
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        // 0 = open, 1 = isolated, 2 = reset. A call that starts after a phase is published must
        // observe that phase or a later one; it may never see an earlier one.
        var phase = 0;
        var stop = 0;
        var violations = 0;
        var calls = new AsyncCounter("rejection storm calls");
        var workers = Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                await Task.Yield();
                var observedPhase = Volatile.Read(ref phase);
                var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
                var valid = outcome.Exception switch
                {
                    null => true,
                    CircuitOpenException { IsIsolated: true, RetryAfter: null } => observedPhase <= 1,
                    CircuitOpenException { IsIsolated: false, RetryAfter: not null } => observedPhase == 0,
                    _ => false,
                };
                if (!valid)
                {
                    Interlocked.Increment(ref violations);
                }

                calls.Signal();
            }
        })).ToArray();

        await calls.WaitForAsync(1_000);
        await monitor.IsolateAsync();
        Volatile.Write(ref phase, 1);
        await calls.WaitForAsync(calls.Count + 1_000);
        await monitor.ResetAsync();
        Volatile.Write(ref phase, 2);
        await calls.WaitForAsync(calls.Count + 1_000);
        Volatile.Write(ref stop, 1);
        await Task.WhenAll(workers);

        await Assert.That(Volatile.Read(ref violations)).IsEqualTo(0);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task Rejection_Storm_Across_The_Break_Deadline_Admits_Exactly_The_Configured_Probes(int probes)
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMilliseconds(100);
            options.HalfOpenProbes = probes;
            options.Monitor = monitor;
        });
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        var probeGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = new AsyncCounter("admitted probes");
        var rejections = new AsyncCounter("rejections after the probes");
        var stop = 0;
        var released = 0;
        var invalid = 0;
        var workers = Enumerable.Range(0, Workers).Select(_ => Task.Run(async () =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                await Task.Yield();
                var outcome = await shield.ExecuteOutcomeAsync(_ =>
                {
                    if (admitted.Signal() > probes && Volatile.Read(ref released) == 0)
                    {
                        Interlocked.Increment(ref invalid);
                    }

                    return new ValueTask<int>(probeGate.Task);
                });
                if (outcome.Exception is CircuitOpenException { IsIsolated: false } rejection)
                {
                    // Open rejections carry the remaining break; saturated half-open ones do not.
                    if (admitted.Count >= probes)
                    {
                        rejections.Signal();
                    }

                    if (rejection.RetryAfter is { } retryAfter
                        && (retryAfter < TimeSpan.Zero || retryAfter > TimeSpan.FromMilliseconds(100)))
                    {
                        Interlocked.Increment(ref invalid);
                    }
                }
                else if (!outcome.IsSuccess)
                {
                    Interlocked.Increment(ref invalid);
                }
            }
        })).ToArray();

        await admitted.WaitForAsync(probes);
        await rejections.WaitForAsync(1_000);
        await Assert.That(admitted.Count).IsEqualTo(probes);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);

        Volatile.Write(ref released, 1);
        probeGate.SetResult(42);
        Volatile.Write(ref stop, 1);
        await Task.WhenAll(workers);

        await Assert.That(Volatile.Read(ref invalid)).IsEqualTo(0);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Abandoned_Probe_Withdraws_The_Saturated_HalfOpen_Rejection()
    {
        var fakeTime = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var failure = new InvalidOperationException("downstream outage");
        var shield = Shield
            .When<InvalidOperationException>()
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDuration = TimeSpan.FromSeconds(1);
                options.Monitor = monitor;
            })
            .WithTimeProvider(fakeTime);
        await shield.ExecuteOutcomeAsync<int>(_ => throw failure);
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        var probeGate = new AsyncGate("probe");
        var probe = shield.ExecuteOutcomeAsync<int>(async _ =>
        {
            await probeGate.EnterAsync();
            throw new ArgumentException("unhandled");
        }).AsTask();
        await probeGate.WaitForEntryAsync();

        var saturated = await RejectAsync(shield);
        await Assert.That(saturated.IsIsolated).IsFalse();
        await Assert.That(saturated.RetryAfter).IsNull();
        await Assert.That(saturated.InnerException).IsSameReferenceAs(failure);

        probeGate.Release();
        await probe;
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Provider_First_Observed_After_Lock_Free_Rejections_Anchors_At_The_Last_Rejection()
    {
        // Every locked rejection used to advance the shared timeline, so a provider first observed
        // later anchored at the last rejection's reading: not at the opening sample, and not at
        // the time of the switch. Lock-free rejections must leave the same anchor behind.
        var breakDuration = TimeSpan.FromSeconds(5);
        var shield = Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: breakDuration);
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await Task.Delay(TimeSpan.FromSeconds(1));
        var lastRejection = await RejectAsync(shield);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var fakeTime = new FakeTimeProvider();
        var fakeCopy = shield.WithTimeProvider(fakeTime);
        var anchored = await RejectAsync(fakeCopy);

        var drift = (anchored.RetryAfter!.Value - lastRejection.RetryAfter!.Value).Duration();
        await Assert.That(drift).IsLessThan(TimeSpan.FromMilliseconds(5));

        fakeTime.Advance(anchored.RetryAfter.Value + TimeSpan.FromMilliseconds(1));
        var result = await fakeCopy.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Provider_First_Observed_Without_An_Intervening_Rejection_Anchors_At_The_Opening_Sample()
    {
        // No rejection ran between opening on the system clock and the switch, so there is no
        // skipped reading to recover: the new provider anchors where the circuit opened.
        var breakDuration = TimeSpan.FromMilliseconds(200);
        var shield = Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: breakDuration);
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        var rejection = await RejectAsync(shield.WithTimeProvider(new FakeTimeProvider()));

        await Assert.That(rejection.RetryAfter!.Value > TimeSpan.FromMilliseconds(190)).IsTrue();
    }

    [Test]
    public async Task Provider_First_Observed_While_A_Custom_Provider_Holds_The_Circuit_Open_Ignores_The_System_Clock()
    {
        // Once a custom provider is observed, open rejections always take the gate, so no system
        // reading can be missing from the timeline. A later provider must anchor at the last
        // recorded sample even if the system clock has run past the break deadline meanwhile.
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMilliseconds(200);
            options.Monitor = monitor;
        });
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await monitor.ResetAsync();

        var frozenTime = new FakeTimeProvider();
        await shield.WithTimeProvider(frozenTime)
            .ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        var rejection = await RejectAsync(shield.WithTimeProvider(new FakeTimeProvider()));

        await Assert.That(rejection.RetryAfter!.Value > TimeSpan.FromMilliseconds(190)).IsTrue();
    }

    private static async Task<CircuitOpenException> RejectAsync(Shield shield)
    {
        var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        return outcome.Exception as CircuitOpenException
            ?? throw new InvalidOperationException($"Expected a circuit rejection but got {outcome}.");
    }
}

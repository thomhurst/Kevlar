using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CircuitBreakerStateReportingTests
{
    [Test]
    public async Task State_Reports_HalfOpen_After_Break_Elapses_Without_A_Call()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var transitions = new List<(CircuitState From, CircuitState To)>();
        var shield = CreateBreaker(timeProvider, monitor, change =>
            transitions.Add((change.From, change.To)));

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
        await Assert.That(transitions).IsEquivalentTo(
        [
            (CircuitState.Closed, CircuitState.Open),
        ]);

        await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(transitions).IsEquivalentTo(
        [
            (CircuitState.Closed, CircuitState.Open),
            (CircuitState.Open, CircuitState.HalfOpen),
            (CircuitState.HalfOpen, CircuitState.Closed),
        ]);
    }

    [Test]
    public async Task Sync_State_Reports_HalfOpen_After_Break_Elapses()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = CreateBreaker(timeProvider, monitor);

        await Assert.That(() => shield.Execute<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
        await Assert.That(shield.Execute(_ => 42)).IsEqualTo(42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Isolated_State_Is_Never_Time_Based()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        _ = CreateBreaker(timeProvider, monitor);

        monitor.Isolate();
        timeProvider.Advance(TimeSpan.FromDays(365));

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Isolated);
    }

    [Test]
    public async Task Stale_Success_Does_Not_Close_HalfOpen()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = CreateBreaker(timeProvider, monitor);
        var staleStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var stale = shield.ExecuteAsync<int>(async _ =>
        {
            staleStarted.SetResult();
            await releaseStale.Task;
            return 1;
        }).AsTask();
        await staleStarted.Task;

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("open"));
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var probe = shield.ExecuteAsync<int>(async _ =>
        {
            probeStarted.SetResult();
            await releaseProbe.Task;
            return 2;
        }).AsTask();
        await probeStarted.Task;

        releaseStale.SetResult();
        await stale;

        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);

        releaseProbe.SetResult();
        await probe;

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Stale_Failure_Does_Not_Reopen_HalfOpen()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var transitions = new List<(CircuitState From, CircuitState To)>();
        var shield = CreateBreaker(timeProvider, monitor, change =>
            transitions.Add((change.From, change.To)));
        var staleStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var stale = shield.ExecuteOutcomeAsync<int>(async _ =>
        {
            staleStarted.SetResult();
            await releaseStale.Task;
            throw new InvalidOperationException("stale");
        }).AsTask();
        await staleStarted.Task;

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("open"));
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var probe = shield.ExecuteAsync<int>(async _ =>
        {
            probeStarted.SetResult();
            await releaseProbe.Task;
            return 2;
        }).AsTask();
        await probeStarted.Task;

        releaseStale.SetResult();
        await stale;

        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
        await Assert.That(transitions).DoesNotContain(
            (CircuitState.HalfOpen, CircuitState.Open));

        releaseProbe.SetResult();
        await probe;

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Probe_Generation_Changes_Across_Reopens()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = CreateBreaker(timeProvider, monitor);
        var firstProbeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondProbeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("first open"));
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var firstProbe = shield.ExecuteAsync<int>(async _ =>
        {
            firstProbeStarted.SetResult();
            await releaseFirstProbe.Task;
            return 1;
        }).AsTask();
        await firstProbeStarted.Task;

        monitor.Reset();
        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("second open"));
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var secondProbe = shield.ExecuteAsync<int>(async _ =>
        {
            secondProbeStarted.SetResult();
            await releaseSecondProbe.Task;
            return 2;
        }).AsTask();
        await secondProbeStarted.Task;

        releaseFirstProbe.SetResult();
        await firstProbe;
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);

        releaseSecondProbe.SetResult();
        await secondProbe;
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Closed_Circuit_Does_Not_Retain_The_Previous_Exception()
    {
        var (shield, weakException) = await OpenAndCloseCircuit();

        CollectGarbage();
        await Assert.That(weakException.IsAlive).IsFalse();

        var current = new ApplicationException("current");
        await shield.ExecuteOutcomeAsync<int>(_ => throw current);
        var rejection = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));

        await Assert.That(ReferenceEquals(rejection.Exception!.InnerException, current)).IsTrue();
    }

    [Test]
    public async Task RetryAfter_Tracks_The_Remaining_Break_Duration()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = CreateBreaker(timeProvider, monitor);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        var initial = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        await Assert.That(((CircuitOpenException)initial.Exception!).RetryAfter)
            .IsEqualTo(TimeSpan.FromSeconds(30));

        timeProvider.Advance(TimeSpan.FromSeconds(12));
        var later = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        await Assert.That(((CircuitOpenException)later.Exception!).RetryAfter)
            .IsEqualTo(TimeSpan.FromSeconds(18));

        timeProvider.Advance(TimeSpan.FromSeconds(18));
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);
    }

    private static Shield CreateBreaker(
        TimeProvider timeProvider,
        CircuitBreakerMonitor monitor,
        Action<CircuitBreakerStateChangedEvent>? onStateChanged = null) => Shield
        .CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(30);
            options.Monitor = monitor;
            options.OnStateChanged = onStateChanged;
        })
        .WithTimeProvider(timeProvider);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(Shield Shield, WeakReference Exception)>
        OpenAndCloseCircuit()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = CreateBreaker(timeProvider, monitor);
        var previous = new InvalidOperationException("previous");
        var weakException = new WeakReference(previous);

        await shield.ExecuteOutcomeAsync<int>(_ => throw previous);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        return (shield, weakException);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

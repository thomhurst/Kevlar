using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Testing.Tests;

public class StateProbeTests
{
    [Test]
    public async Task Circuit_Snapshot_Reports_Time_Derived_HalfOpen_State()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDuration = TimeSpan.FromSeconds(30);
                options.Monitor = monitor;
            })
            .WithTimeProvider(timeProvider);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        var snapshot = shield.GetStateSnapshot().Strategies
            .OfType<CircuitBreakerStateSnapshot>()
            .Single();
        await Assert.That(snapshot.State).IsEqualTo(CircuitState.HalfOpen);
        await Assert.That(snapshot.State).IsEqualTo(monitor.State);
    }

    [Test]
    public async Task StateSnapshot_Reports_Circuit_And_Limiter_State()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.Monitor = monitor;
            })
            .RateLimit(options =>
            {
                options.Permits = 2;
                options.Burst = 2;
                options.Window = TimeSpan.FromSeconds(1);
                options.QueueLimit = 1;
            })
            .ConcurrencyLimit(2, queueLimit: 1)
            .WithTimeProvider(timeProvider);

        monitor.Isolate();
        var snapshot = shield.GetStateSnapshot();

        await Assert.That(snapshot.Strategies).Count().IsEqualTo(3);

        var circuit = snapshot.Strategies.OfType<CircuitBreakerStateSnapshot>().Single();
        await Assert.That(circuit.StrategyIndex).IsEqualTo(0);
        await Assert.That(circuit.State).IsEqualTo(CircuitState.Isolated);

        var rate = snapshot.Strategies.OfType<RateLimitStateSnapshot>().Single();
        await Assert.That(rate.StrategyIndex).IsEqualTo(1);
        await Assert.That(rate.AvailablePermits).IsEqualTo(2L);
        await Assert.That(rate.QueuedExecutions).IsEqualTo(0);

        var concurrency = snapshot.Strategies.OfType<ConcurrencyLimitStateSnapshot>().Single();
        await Assert.That(concurrency.StrategyIndex).IsEqualTo(2);
        await Assert.That(concurrency.AvailablePermits).IsEqualTo(2);
        await Assert.That(concurrency.RunningExecutions).IsEqualTo(0);
        await Assert.That(concurrency.QueuedExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task StateSnapshot_Tracks_Rate_Queue_With_Fake_Time()
    {
        var timeProvider = new FakeTimeProvider();
        var shield = Shield
            .RateLimit(options =>
            {
                options.Permits = 1;
                options.Burst = 1;
                options.Window = TimeSpan.FromSeconds(1);
                options.QueueLimit = 1;
            })
            .WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(static _ => ValueTask.CompletedTask);
        var queuedExecution = shield.ExecuteAsync(static _ => ValueTask.CompletedTask).AsTask();

        var queued = shield.GetStateSnapshot().Strategies.OfType<RateLimitStateSnapshot>().Single();
        await Assert.That(queued.AvailablePermits).IsEqualTo(0L);
        await Assert.That(queued.QueuedExecutions).IsEqualTo(1);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await queuedExecution.WaitAsync(TimeSpan.FromSeconds(5));

        var completed = shield.GetStateSnapshot().Strategies.OfType<RateLimitStateSnapshot>().Single();
        await Assert.That(completed.QueuedExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task StateSnapshot_Remains_Coherent_For_Shared_Composed_State()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = Shield.ConcurrencyLimit(1, queueLimit: 1);
        var firstAlias = Shield.Compose(shared).WithName("first");
        var secondAlias = Shield.Compose(shared).WithName("second");

        var running = firstAlias.ExecuteAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
        }).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var queued = secondAlias.ExecuteAsync(static _ => ValueTask.CompletedTask).AsTask();
        var firstSnapshot = firstAlias.GetStateSnapshot().Strategies
            .OfType<ConcurrencyLimitStateSnapshot>().Single();
        var secondSnapshot = secondAlias.GetStateSnapshot().Strategies
            .OfType<ConcurrencyLimitStateSnapshot>().Single();

        await Assert.That(firstSnapshot.RunningExecutions).IsEqualTo(1);
        await Assert.That(firstSnapshot.QueuedExecutions).IsEqualTo(1);
        await Assert.That(secondSnapshot.AvailablePermits).IsEqualTo(firstSnapshot.AvailablePermits);
        await Assert.That(secondSnapshot.RunningExecutions).IsEqualTo(firstSnapshot.RunningExecutions);
        await Assert.That(secondSnapshot.QueuedExecutions).IsEqualTo(firstSnapshot.QueuedExecutions);

        release.TrySetResult();
        await Task.WhenAll(running, queued).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ExecutionProbe_Counts_Typed_And_Untyped_Attempts()
    {
        var untypedProbe = new ExecutionProbe();
        var untypedInvocations = 0;
        var untypedShield = Shield.When<InvalidOperationException>().Retry(2, Backoff.None);

        await untypedShield.ExecuteAsync(untypedProbe.Wrap(_ =>
        {
            if (Interlocked.Increment(ref untypedInvocations) < 3)
            {
                throw new InvalidOperationException("retry");
            }

            return ValueTask.CompletedTask;
        }));

        var typedProbe = new ExecutionProbe();
        var typedInvocations = 0;
        var typedShield = Shield.For<int>().WhenResult(static result => result == 0).Retry(1, Backoff.None);
        var result = await typedShield.ExecuteAsync(typedProbe.Wrap<int>(_ =>
            new ValueTask<int>(Interlocked.Increment(ref typedInvocations) - 1)));

        await Assert.That(untypedProbe.AttemptCount).IsEqualTo(3);
        await Assert.That(typedProbe.AttemptCount).IsEqualTo(2);
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task ExecutionProbe_Supports_Outcome_Executions()
    {
        var untypedProbe = new ExecutionProbe();
        var untyped = await Shield.Empty.ExecuteOutcomeAsync(untypedProbe.Wrap(
            static _ => throw new InvalidOperationException("untyped")));

        var typedProbe = new ExecutionProbe();
        var typed = await Shield<int>.Empty.ExecuteOutcomeAsync(typedProbe.Wrap<int>(
            static _ => throw new InvalidOperationException("typed")));

        await Assert.That(untyped.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(typed.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(untypedProbe.AttemptCount).IsEqualTo(1);
        await Assert.That(typedProbe.AttemptCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExecutionProbe_Observes_Active_Attempt_Cancellation()
    {
        var probe = new ExecutionProbe();
        using var cancellation = new CancellationTokenSource();
        var execution = Shield.Empty.ExecuteAsync(probe.Wrap(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
        }), cancellation.Token).AsTask();

        await probe.WaitForAttemptCountAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await execution);
        await probe.WaitForCancellationCountAsync(1).WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = probe.GetSnapshot();
        await Assert.That(snapshot.AttemptCount).IsEqualTo(1);
        await Assert.That(snapshot.CancellationCount).IsEqualTo(1);
    }

    [Test]
    public async Task StateSnapshot_Supports_Typed_Shields_And_Immutable_Collections()
    {
        var shield = Shield.For<int>().ConcurrencyLimit(3, queueLimit: 2);

        var snapshot = shield.GetStateSnapshot();
        var concurrency = snapshot.Strategies.OfType<ConcurrencyLimitStateSnapshot>().Single();

        await Assert.That(concurrency.AvailablePermits).IsEqualTo(3);
        await Assert.That(snapshot.Strategies).IsAssignableTo<IList<StrategyStateSnapshot>>();
        var list = (IList<StrategyStateSnapshot>)snapshot.Strategies;
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Task.Run(() => list.Add(concurrency)));
    }

    [Test]
    public async Task ExecutionProbe_Wait_Preserves_Cancellation_Token()
    {
        var probe = new ExecutionProbe();
        using var cancellation = new CancellationTokenSource();

        var wait = probe.WaitForAttemptCountAsync(1, cancellation.Token);
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await wait);
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task StateSnapshot_Rejects_Null_Typed_And_Untyped_Shields()
    {
        Shield? untyped = null;
        Shield<int>? typed = null;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.Run(() => untyped!.GetStateSnapshot()));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.Run(() => typed!.GetStateSnapshot()));
    }
}

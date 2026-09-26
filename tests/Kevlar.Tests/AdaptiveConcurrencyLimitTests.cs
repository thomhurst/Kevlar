using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class AdaptiveConcurrencyLimitTests
{
    [Test]
    public async Task Healthy_Load_Grows_Once_Per_Window_And_Stops_At_Maximum()
    {
        var time = new FakeTimeProvider();
        var shield = Create(time, initial: 2, maximum: 5);
        for (var expected = 3; expected <= 5; expected++)
        {
            await CompleteHealthyWindow(shield, time);
            await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(expected);
            await Assert.That(Snapshot(shield).RunningExecutions).IsEqualTo(0);
        }

        await CompleteHealthyWindow(shield, time);
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(5);
        for (var index = 0; index < 100; index++)
        {
            _ = shield.Execute(static _ => 42);
        }
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(5);
    }

    [Test]
    [Arguments("failure")]
    [Arguments("timeout")]
    [Arguments("rejection")]
    public async Task Downstream_Failures_Shrink_At_Window_Boundaries_And_Stop_At_Minimum(string kind)
    {
        var time = new FakeTimeProvider();
        var shield = Create(time, initial: 4);
        var expected = kind switch
        {
            "timeout" => (Exception)new TimeoutExceededException(),
            "rejection" => new ConcurrencyLimitExceededException(),
            _ => new IOException(),
        };
        for (var index = 0; index < 10; index++)
        {
            var outcome = await shield.ExecuteOutcomeAsync<int>(_ => ValueTask.FromException<int>(expected));
            await Assert.That(ReferenceEquals(outcome.Exception, expected)).IsTrue();
        }
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(4);
        time.Advance(TimeSpan.FromSeconds(1));
        _ = await shield.ExecuteOutcomeAsync<int>(_ => ValueTask.FromException<int>(expected));
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(2);
        time.Advance(TimeSpan.FromSeconds(1));
        _ = await shield.ExecuteOutcomeAsync<int>(_ => ValueTask.FromException<int>(expected));
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(1);
        time.Advance(TimeSpan.FromSeconds(1));
        _ = await shield.ExecuteOutcomeAsync<int>(_ => ValueTask.FromException<int>(expected));
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(1);
    }

    [Test]
    public async Task Latency_Spikes_Shrink_The_Limit_Without_Changing_Results()
    {
        var time = new FakeTimeProvider();
        var shield = Create(time, initial: 4);
        _ = ExecuteTimed(shield, time, milliseconds: 1);
        time.Advance(TimeSpan.FromSeconds(1));
        _ = ExecuteTimed(shield, time, milliseconds: 1);
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(4);

        await Assert.That(ExecuteTimed(shield, time, milliseconds: 20)).IsEqualTo(42);
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(4);
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(ExecuteTimed(shield, time, milliseconds: 20)).IsEqualTo(42);
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(2);
    }

    [Test]
    public async Task Underutilized_And_Idle_Windows_Do_Not_Increase_The_Limit()
    {
        var time = new FakeTimeProvider();
        var shield = Create(time, initial: 8);
        _ = ExecuteTimed(shield, time, milliseconds: 1);
        time.Advance(TimeSpan.FromHours(1));
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(8);
        _ = ExecuteTimed(shield, time, milliseconds: 1);
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(8);
    }

    [Test]
    public async Task Shrinking_Preserves_Existing_Executions_And_Rejects_Until_They_Drain()
    {
        var time = new FakeTimeProvider();
        var shield = Create(time, initial: 4);
        _ = ExecuteTimed(shield, time, milliseconds: 1);
        var gates = Enumerable.Range(0, 4).Select(_ => NewGate()).ToArray();
        var executions = gates.Select(gate => shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(gate.Task)).AsTask()).ToArray();
        try
        {
            time.Advance(TimeSpan.FromSeconds(1));
            gates[0].SetException(new IOException());
            _ = await executions[0];
            var snapshot = Snapshot(shield);
            await Assert.That(snapshot.CurrentLimit).IsEqualTo(2);
            await Assert.That(snapshot.RunningExecutions).IsEqualTo(3);
            await Assert.That(snapshot.AvailablePermits).IsEqualTo(0);
            await Assert.That((await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42))).Exception)
                .IsTypeOf<ConcurrencyLimitExceededException>();
            gates[1].SetResult(42);
            _ = await executions[1];
            await Assert.That((await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42))).Exception)
                .IsTypeOf<ConcurrencyLimitExceededException>();
            gates[2].SetResult(42);
            _ = await executions[2];
            await Assert.That(shield.Execute(static _ => 43)).IsEqualTo(43);
        }
        finally
        {
            foreach (var gate in gates)
            {
                gate.TrySetResult(42);
            }
            _ = await Task.WhenAll(executions);
        }
        await Assert.That(Snapshot(shield).RunningExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task Concurrent_Admissions_Respect_The_Limit()
    {
        var time = new FakeTimeProvider();
        var shield = Create(time, initial: 3);
        var gate = NewGate();
        var admitted = 0;
        var attempted = 0;
        var executions = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            var execution = shield.ExecuteOutcomeAsync(_ =>
            {
                Interlocked.Increment(ref admitted);
                return new ValueTask<int>(gate.Task);
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
            await Assert.That(Snapshot(shield).RunningExecutions).IsEqualTo(3);
            await Assert.That(Snapshot(shield).QueuedExecutions).IsEqualTo(0);
        }
        finally
        {
            gate.TrySetResult(42);
        }
        var outcomes = await Task.WhenAll(executions);
        await Assert.That(outcomes.Count(outcome => outcome.IsSuccess)).IsEqualTo(3);
        await Assert.That(outcomes.Count(outcome => outcome.Exception is ConcurrencyLimitExceededException)).IsEqualTo(47);
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(3);
    }

    [Test]
    public async Task Caller_Cancellation_Releases_Permits_Without_Congestion_Feedback()
    {
        var time = new FakeTimeProvider();
        var shield = Create(time, initial: 4);
        _ = ExecuteTimed(shield, time, milliseconds: 1);
        using var cancellation = new CancellationTokenSource();
        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 42;
        }, cancellation.Token).AsTask();
        time.Advance(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var outcome = await execution;
        await Assert.That(outcome.Exception is OperationCanceledException).IsTrue();
        await Assert.That(Snapshot(shield).RunningExecutions).IsEqualTo(0);
        _ = ExecuteTimed(shield, time, milliseconds: 1);
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(4);
    }

    [Test]
    public async Task Rejection_Notification_Uses_The_Current_Limit_And_Is_Awaited()
    {
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedLimit = 0;
        var shield = Shield.For<int>().ConcurrencyLimit(new AdaptiveConcurrencyLimitOptions
        {
            InitialLimit = 1,
            OnRejected = async rejected =>
            {
                observedLimit = rejected.MaxConcurrency;
                called.TrySetResult();
                await releaseCallback.Task;
            },
        });
        var gate = NewGate();
        var running = shield.ExecuteAsync(_ => new ValueTask<int>(gate.Task)).AsTask();
        var rejection = shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(43)).AsTask();
        try
        {
            await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(rejection.IsCompleted).IsFalse();
            await Assert.That(observedLimit).IsEqualTo(1);
        }
        finally
        {
            releaseCallback.TrySetResult();
            gate.TrySetResult(42);
        }
        await Assert.That((await rejection).Exception).IsTypeOf<ConcurrencyLimitExceededException>();
        await Assert.That(await running).IsEqualTo(42);
    }

    [Test]
    public async Task Providers_With_Different_Epochs_Share_A_Monotonic_Window()
    {
        var first = new FakeTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var second = new FakeTimeProvider(new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var shield = Create(first, initial: 2, maximum: 2);
        _ = ExecuteTimed(shield, first, milliseconds: 1);
        first.Advance(TimeSpan.FromSeconds(1));
        _ = ExecuteTimed(shield, first, milliseconds: 1);
        var alias = shield.WithTimeProvider(second);
        _ = await alias.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(2);
        second.Advance(TimeSpan.FromSeconds(1));
        _ = await alias.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        await Assert.That(Snapshot(shield).CurrentLimit).IsEqualTo(1);
    }

    [Test]
    public async Task Public_Factories_Copy_Options_And_Expose_Descriptors()
    {
        var options = new AdaptiveConcurrencyLimitOptions { InitialLimit = 2 };
        var untyped = new[]
        {
            Shield.ConcurrencyLimit(options),
            Shield.Empty.ConcurrencyLimit(options),
            Shield.When<IOException>().ConcurrencyLimit(options),
        };
        var typed = new[]
        {
            Shield.For<int>().ConcurrencyLimit(options),
            Shield.For<int>().When<IOException>().ConcurrencyLimit(options),
        };
        options.InitialLimit = 90;
        foreach (var descriptor in untyped.Select(shield => shield.GetDescriptor())
            .Concat(typed.Select(shield => shield.GetDescriptor())))
        {
            var adaptive = (AdaptiveConcurrencyLimitStrategyDescriptor)descriptor.Strategies.Single();
            await Assert.That(adaptive.InitialLimit).IsEqualTo(2);
            await Assert.That(adaptive.Algorithm).IsEqualTo(AdaptiveConcurrencyLimitAlgorithm.Aimd);
            await Assert.That(adaptive.Kind).IsEqualTo(StrategyKind.ConcurrencyLimit);
        }
        await Assert.That(untyped[0].ToString()).IsEqualTo("AdaptiveConcurrencyLimit(AIMD, initial 2, min 1, max 100, window 1s)");
        await Assert.That(typed[0].Execute(static _ => 42)).IsEqualTo(42);
        await Assert.That(Snapshot(Shield.ConcurrencyLimit(7)).CurrentLimit).IsEqualTo(7);
    }

    private static Shield Create(FakeTimeProvider time, int initial, int maximum = 10) =>
        Shield.ConcurrencyLimit(new AdaptiveConcurrencyLimitOptions
        {
            InitialLimit = initial,
            MaxLimit = maximum,
            DecreaseFactor = 0.5,
            SamplingWindow = TimeSpan.FromSeconds(1),
        }).WithTimeProvider(time);

    private static TaskCompletionSource<int> NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ConcurrencyLimitStateSnapshot Snapshot(Shield shield) =>
        shield.GetStateSnapshot().Strategies.OfType<ConcurrencyLimitStateSnapshot>().Single();

    private static int ExecuteTimed(Shield shield, FakeTimeProvider time, int milliseconds) =>
        shield.Execute(_ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            return 42;
        });

    private static async Task CompleteHealthyWindow(Shield shield, FakeTimeProvider time)
    {
        var gates = Enumerable.Range(0, Snapshot(shield).CurrentLimit).Select(_ => NewGate()).ToArray();
        var executions = gates.Select(gate => shield.ExecuteAsync(_ => new ValueTask<int>(gate.Task)).AsTask()).ToArray();
        try
        {
            time.Advance(TimeSpan.FromMilliseconds(1));
        }
        finally
        {
            foreach (var gate in gates)
            {
                gate.TrySetResult(42);
            }
        }
        _ = await Task.WhenAll(executions);
        time.Advance(TimeSpan.FromSeconds(1));
        _ = ExecuteTimed(shield, time, milliseconds: 1);
    }
}

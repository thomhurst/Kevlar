namespace Kevlar.Tests;

public class TestInfrastructureTests
{
    [Test]
    public async Task Gate_Reports_Entry_And_Requires_Explicit_Release()
    {
        var gate = new AsyncGate("helper test");
        var pending = gate.EnterAsync();

        await gate.WaitForEntryAsync();
        await Assert.That(pending.IsCompleted).IsFalse();

        gate.Release();
        await pending;
    }

    [Test]
    public async Task Barrier_Reports_All_Participants_Before_Release()
    {
        var barrier = new AsyncBarrier("helper test", 2);
        var first = barrier.SignalAndWaitAsync();
        var second = barrier.SignalAndWaitAsync();

        await barrier.WaitForAllAsync();
        await Assert.That(barrier.ArrivedCount).IsEqualTo(2);
        await Assert.That(first.IsCompleted).IsFalse();
        await Assert.That(second.IsCompleted).IsFalse();

        barrier.Release();
        await Task.WhenAll(first, second);
    }

    [Test]
    public async Task Cancellation_Probe_Reports_Registration_Execution()
    {
        using var source = new CancellationTokenSource();
        using var probe = new CancellationProbe(source.Token);

        source.Cancel();

        await probe.WaitAsync();
        await Assert.That(probe.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Controlled_Timer_Callback_Can_Outlive_Timer_Disposal()
    {
        var provider = new ControlledTimeProvider();
        var fired = 0;
        var timer = provider.CreateTimer(_ => Interlocked.Increment(ref fired), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        await provider.WaitForTimersAsync(1);
        var queued = provider.QueueTimerCallback(0);

        timer.Dispose();
        await Assert.That(provider.IsTimerDisposed(0)).IsTrue();
        await Assert.That(provider.QueuedCallbackCount).IsEqualTo(1);

        queued.Fire();

        await Assert.That(fired).IsEqualTo(1);
        await Assert.That(queued.IsPending).IsFalse();
        await Assert.That(provider.QueuedCallbackCount).IsEqualTo(0);
    }

    [Test]
    public async Task Race_Runner_Names_And_Exercises_Both_Orders()
    {
        var orders = new List<RaceOrder>();
        var traces = new List<(RaceOrder Order, string Trace)>();

        await RaceRunner.RunBothOrdersAsync("helper ordering", 4, iteration =>
        {
            orders.Add(iteration.Order);
            var trace = new List<string>();
            iteration.Apply(() => trace.Add("first"), () => trace.Add("second"));
            traces.Add((iteration.Order, string.Join(",", trace)));
            return Task.CompletedTask;
        }, seed: 1234);

        await Assert.That(orders.Count).IsEqualTo(8);
        await Assert.That(orders.Count(order => order == RaceOrder.FirstThenSecond)).IsEqualTo(4);
        await Assert.That(orders.Count(order => order == RaceOrder.SecondThenFirst)).IsEqualTo(4);
        await Assert.That(traces.All(item => item.Trace == (item.Order == RaceOrder.FirstThenSecond
            ? "first,second"
            : "second,first"))).IsTrue();
    }

    [Test]
    public async Task Race_Runner_Preserves_Reproduction_Data_In_Failures()
    {
        var exception = await Assert.That(async () => await RaceRunner.RunBothOrdersAsync(
            "reproducible failure",
            repetitions: 1,
            iteration => throw new InvalidOperationException(iteration.CreateRandom().Next().ToString()),
            seed: 2468)).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("race 'reproducible failure'");
        await Assert.That(exception.Message).Contains("seed 2468");
        await Assert.That(exception.Message).Contains("order");
    }

    [Test]
    public async Task Model_Runner_Reports_Seed_And_Minimized_Commands()
    {
        var exception = await Assert.That(async () => await ModelRunner.RunAsync(
            "reproducible model",
            commandCount: 4,
            _ => "step",
            commands => commands.Count >= 2
                ? throw new InvalidOperationException("failure")
                : Task.CompletedTask)).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("Model 'reproducible model' failed with seed 0");
        await Assert.That(exception.Message).Contains("Minimized commands: [step, step]");
    }
}

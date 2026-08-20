using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CircuitBreakerTests
{
    [Test]
    public async Task Opens_After_Consecutive_Failures()
    {
        var attempts = 0;
        var shield = Shield.CircuitBreaker(2, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 2; i++)
        {
            await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
            {
                attempts++;
                throw new InvalidOperationException();
            })).Throws<InvalidOperationException>();
        }

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            return new ValueTask<int>(1);
        })).Throws<CircuitOpenException>();

        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task HalfOpen_Probe_Success_Closes_The_Circuit()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(30)).WithTimeProvider(fakeTime);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        var probeResult = await shield.ExecuteAsync(_ => new ValueTask<int>(7));
        await Assert.That(probeResult).IsEqualTo(7);

        var subsequent = await shield.ExecuteAsync(_ => new ValueTask<int>(8));
        await Assert.That(subsequent).IsEqualTo(8);
    }

    [Test]
    public async Task HalfOpen_Probe_Failure_Reopens_The_Circuit()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(1, TimeSpan.FromSeconds(30)).WithTimeProvider(fakeTime);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Failure_Ratio_Mode_Opens_When_Threshold_Reached()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.FailureRatio = 0.5;
                options.MinimumThroughput = 4;
                options.SamplingWindow = TimeSpan.FromSeconds(10);
                options.BreakDuration = TimeSpan.FromMinutes(1);
            })
            .WithTimeProvider(fakeTime);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Monitor_Supports_Isolate_And_Reset()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 5;
            options.Monitor = monitor;
        });

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        monitor.Isolate();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Isolated);

        var exception = await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
        await Assert.That(exception!.IsIsolated).IsTrue();

        monitor.Reset();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(9));
        await Assert.That(result).IsEqualTo(9);
    }

    [Test]
    public async Task State_Transitions_Are_Published()
    {
        var fakeTime = new FakeTimeProvider();
        var transitions = new List<(CircuitState From, CircuitState To)>();
        var shield = Shield
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDuration = TimeSpan.FromSeconds(10);
                options.OnStateChanged = change => transitions.Add((change.From, change.To));
            })
            .WithTimeProvider(fakeTime);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        fakeTime.Advance(TimeSpan.FromSeconds(10));
        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(transitions).IsEquivalentTo(
        [
            (CircuitState.Closed, CircuitState.Open),
            (CircuitState.Open, CircuitState.HalfOpen),
            (CircuitState.HalfOpen, CircuitState.Closed),
        ]);
    }

    [Test]
    public async Task Cancelled_Executions_Do_Not_Move_The_Circuit()
    {
        var shield = Shield.CircuitBreaker(2, TimeSpan.FromMinutes(1));

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(0);
        await Task.Delay(10);

        await Assert.That(async () => await shield.ExecuteAsync<int>(
            async token =>
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                return 1;
            }, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(async () => await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Ambient_Handling_Scopes_What_Trips_The_Circuit()
    {
        var shield = Shield.When<InvalidOperationException>().CircuitBreaker(1, TimeSpan.FromMinutes(1));

        // Unhandled exception types pass through without tripping the circuit.
        for (var i = 0; i < 3; i++)
        {
            await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new ArgumentException()))
                .Throws<ArgumentException>();
        }

        var stillClosed = await shield.ExecuteAsync(_ => new ValueTask<int>(5));
        await Assert.That(stillClosed).IsEqualTo(5);
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CircuitBreakerTransitionOrderingTests
{
    [Test]
    public async Task Concurrent_Control_Publishes_One_Ordered_NonConcurrent_Stream()
    {
        var monitor = new CircuitBreakerMonitor();
        var optionTransitions = new ConcurrentQueue<(CircuitState From, CircuitState To)>();
        var monitorTransitions = new ConcurrentQueue<(CircuitState From, CircuitState To)>();
        var openingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOpening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackGate = new object();
        var activeCallbacks = 0;
        var maximumConcurrentCallbacks = 0;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChanged = change =>
            {
                var active = Interlocked.Increment(ref activeCallbacks);
                lock (callbackGate)
                {
                    maximumConcurrentCallbacks = Math.Max(maximumConcurrentCallbacks, active);
                }

                try
                {
                    optionTransitions.Enqueue((change.From, change.To));
                    if (change is { From: CircuitState.Closed, To: CircuitState.Open })
                    {
                        openingEntered.TrySetResult();
                        releaseOpening.Task.GetAwaiter().GetResult();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref activeCallbacks);
                }
            };
        });
        monitor.StateChanged += change => monitorTransitions.Enqueue((change.From, change.To));

        var opening = Task.Run(async () =>
            await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException()));
        await openingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var reset = Task.Run(monitor.Reset);
        await WaitForStateAsync(monitor, CircuitState.Closed);
        var isolate = Task.Run(monitor.Isolate);
        await WaitForStateAsync(monitor, CircuitState.Isolated);

        releaseOpening.TrySetResult();
        await Task.WhenAll(opening, reset, isolate).WaitAsync(TimeSpan.FromSeconds(5));

        (CircuitState From, CircuitState To)[] expected =
        [
            (CircuitState.Closed, CircuitState.Open),
            (CircuitState.Open, CircuitState.Closed),
            (CircuitState.Closed, CircuitState.Isolated),
        ];
        await Assert.That(optionTransitions.SequenceEqual(expected)).IsTrue();
        await Assert.That(monitorTransitions.SequenceEqual(expected)).IsTrue();
        await Assert.That(maximumConcurrentCallbacks).IsEqualTo(1);
    }

    [Test]
    public async Task Throwing_Option_Callback_Does_Not_Skip_Monitor_Observer()
    {
        var monitor = new CircuitBreakerMonitor();
        var callbackFailure = new InvalidOperationException("option callback");
        CircuitStateChangedEvent? observed = null;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChanged = _ => throw callbackFailure;
        });
        monitor.StateChanged += change => observed = change;

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException("operation")))
            .Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(thrown, callbackFailure)).IsTrue();
        await Assert.That(observed?.To).IsEqualTo(CircuitState.Open);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Throwing_Monitor_Observer_Leaves_The_Circuit_Usable()
    {
        var monitor = new CircuitBreakerMonitor();
        var monitorFailure = new InvalidOperationException("monitor callback");
        var optionObserved = false;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChanged = change => optionObserved |= change.To == CircuitState.Open;
        });
        monitor.StateChanged += change =>
        {
            if (change.To == CircuitState.Open)
            {
                throw monitorFailure;
            }
        };

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException("operation")))
            .Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(thrown, monitorFailure)).IsTrue();
        await Assert.That(optionObserved).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);

        monitor.Reset();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Failures_From_Both_Observers_Are_Aggregated()
    {
        var monitor = new CircuitBreakerMonitor();
        var optionFailure = new InvalidOperationException("option callback");
        var monitorFailure = new InvalidOperationException("monitor callback");
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChanged = _ => throw optionFailure;
        });
        monitor.StateChanged += _ => throw monitorFailure;

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException("operation")))
            .Throws<AggregateException>();

        await Assert.That(thrown!.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(thrown.InnerExceptions[0], optionFailure)).IsTrue();
        await Assert.That(ReferenceEquals(thrown.InnerExceptions[1], monitorFailure)).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task HalfOpen_Observer_Failure_Releases_The_Probe_Slot()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var callbackFailure = new InvalidOperationException("half-open callback");
        var failHalfOpenCallback = true;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
            options.OnStateChanged = change =>
            {
                if (change.To == CircuitState.HalfOpen && failHalfOpenCallback)
                {
                    failHalfOpenCallback = false;
                    throw callbackFailure;
                }
            };
        }).WithTimeProvider(timeProvider);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("operation"));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, callbackFailure)).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Reentrant_Control_Is_Queued_After_The_Current_Transition()
    {
        var monitor = new CircuitBreakerMonitor();
        var optionTransitions = new List<(CircuitState From, CircuitState To)>();
        var monitorTransitions = new List<(CircuitState From, CircuitState To)>();
        var statesReadFromCallback = new List<CircuitState>();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChanged = change =>
            {
                optionTransitions.Add((change.From, change.To));
                statesReadFromCallback.Add(monitor.State);
                if (change.To == CircuitState.Open)
                {
                    monitor.Reset();
                }
            };
        });
        monitor.StateChanged += change => monitorTransitions.Add((change.From, change.To));

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        (CircuitState From, CircuitState To)[] expected =
        [
            (CircuitState.Closed, CircuitState.Open),
            (CircuitState.Open, CircuitState.Closed),
        ];
        await Assert.That(optionTransitions.SequenceEqual(expected)).IsTrue();
        await Assert.That(monitorTransitions.SequenceEqual(expected)).IsTrue();
        await Assert.That(statesReadFromCallback.SequenceEqual(
            [CircuitState.Open, CircuitState.Closed])).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Repeated_Failure_Storms_Produce_One_Opening_Transition()
    {
        for (var iteration = 0; iteration < 25; iteration++)
        {
            var openingTransitions = 0;
            var shield = Shield.CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 3;
                options.BreakDuration = TimeSpan.FromMinutes(1);
                options.OnStateChanged = change =>
                {
                    if (change.To == CircuitState.Open)
                    {
                        Interlocked.Increment(ref openingTransitions);
                    }
                };
            });

            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
                shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException()).AsTask()));

            await Assert.That(openingTransitions).IsEqualTo(1);
        }
    }

    private static async Task WaitForStateAsync(CircuitBreakerMonitor monitor, CircuitState expected)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (monitor.State != expected && DateTime.UtcNow < timeout)
        {
            await Task.Yield();
        }

        await Assert.That(monitor.State).IsEqualTo(expected);
    }
}

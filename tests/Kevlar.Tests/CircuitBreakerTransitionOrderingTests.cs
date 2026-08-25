using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CircuitBreakerTransitionOrderingTests
{
    [Test]
    public async Task Reentrant_Execution_Transition_Preserves_Triggering_Context()
    {
        var key = new KevlarKey<string>("reentrant-transition");
        var monitor = new CircuitBreakerMonitor();
        var opened = 0;
        string? observed = null;
        Shield shield = null!;
        shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChanged = change =>
            {
                if (change.To != CircuitState.Open)
                {
                    return;
                }

                if (Interlocked.Increment(ref opened) == 1)
                {
                    monitor.Reset();
                    try
                    {
                        shield.ExecuteWithContext<int>(context =>
                        {
                            context.Properties.Set(key, "nested");
                            throw new InvalidOperationException("nested");
                        });
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return;
                }

                observed = change.Context.Properties.GetOrDefault<string>(key);
            };
        });

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("outer"));

        await Assert.That(observed).IsEqualTo("nested");
    }

    [Test]
    public async Task Async_Reentrant_Execution_Transition_Preserves_Triggering_Context()
    {
        var key = new KevlarKey<string>("async-reentrant-transition");
        var monitor = new CircuitBreakerMonitor();
        var opened = 0;
        string? observed = null;
        Shield shield = null!;
        shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChangedAsync = async change =>
            {
                if (change.To != CircuitState.Open)
                {
                    return;
                }

                if (Interlocked.Increment(ref opened) == 1)
                {
                    await monitor.ResetAsync();
                    try
                    {
                        await shield.ExecuteWithContextAsync<int>(context =>
                        {
                            context.Properties.Set(key, "nested");
                            return ValueTask.FromException<int>(new InvalidOperationException("nested"));
                        });
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return;
                }

                observed = change.Context.Properties.GetOrDefault<string>(key);
            };
        });

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("outer"));

        await Assert.That(observed).IsEqualTo("nested");
    }

    [Test]
    public async Task Suppressed_Context_Flow_Preserves_Reentrant_Publication_Parent()
    {
        var monitor = new CircuitBreakerMonitor();
        var transitions = new List<(CircuitState From, CircuitState To)>();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChangedAsync = change =>
            {
                transitions.Add((change.From, change.To));
                if (change.To == CircuitState.Open)
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        monitor.Reset();
                    }
                }

                return default;
            };
        });

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException())
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(transitions.SequenceEqual(
        [
            (CircuitState.Closed, CircuitState.Open),
            (CircuitState.Open, CircuitState.Closed),
        ])).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

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
        CircuitBreakerStateChangedEvent? observed = null;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChanged = _ => throw callbackFailure;
        });
        monitor.StateChanged += change => observed = change;

        var operationFailure = new InvalidOperationException("operation");
        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw operationFailure))
            .Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(thrown, operationFailure)).IsTrue();
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

        var operationFailure = new InvalidOperationException("operation");
        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw operationFailure))
            .Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(thrown, operationFailure)).IsTrue();
        await Assert.That(optionObserved).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);

        monitor.Reset();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Failures_From_Both_Observers_Do_Not_Replace_The_Operation()
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

        var operationFailure = new InvalidOperationException("operation");
        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw operationFailure))
            .Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(thrown, operationFailure)).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    [NotInParallel]
    public async Task Throwing_Metrics_Listener_Does_Not_Stall_Later_Transitions()
    {
        var monitor = new CircuitBreakerMonitor();
        var metricsFailure = new InvalidOperationException("metrics callback");
        var observed = new List<CircuitState>();
        _ = Shield.CircuitBreaker(options =>
        {
            options.Monitor = monitor;
            options.OnStateChanged = change => observed.Add(change.To);
        });

        using (var listener = new MeterListener())
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument is Counter<long> { Name: "kevlar.circuit_breaker.transitions" })
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag is { Key: "kevlar.circuit_breaker.state.to", Value: "isolated" })
                    {
                        throw metricsFailure;
                    }
                }
            });
            listener.Start();

            var thrown = await Assert.That(() => monitor.Isolate()).Throws<InvalidOperationException>();
            await Assert.That(ReferenceEquals(thrown, metricsFailure)).IsTrue();
        }

        await Task.Run(monitor.Reset).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(observed.SequenceEqual([CircuitState.Isolated, CircuitState.Closed])).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task HalfOpen_Observer_Failure_Does_Not_Abandon_The_Probe()
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

        var probe = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(probe).IsEqualTo(1);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task HalfOpen_Observer_Failure_Does_Not_Release_A_Newer_Probe()
    {
        var timeProvider = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var callbackFailure = new InvalidOperationException("half-open callback");
        var releaseNewProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? newProbe = null;
        var arrangeReentrantProbe = true;
        Shield? shield = null;
        shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
            options.OnStateChanged = change =>
            {
                if (change.To != CircuitState.HalfOpen || !arrangeReentrantProbe)
                {
                    return;
                }

                arrangeReentrantProbe = false;
                monitor.Reset();
                shield!.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("reopen"))
                    .AsTask().GetAwaiter().GetResult();
                timeProvider.Advance(TimeSpan.FromSeconds(1));
                newProbe = shield.ExecuteAsync(async _ =>
                {
                    await releaseNewProbe.Task;
                    return 42;
                }).AsTask();
                throw callbackFailure;
            };
        }).WithTimeProvider(timeProvider);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("open"));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var staleProbe = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(staleProbe).IsEqualTo(1);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.HalfOpen);

        var rejected = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(2));
        await Assert.That(rejected.Exception).IsTypeOf<CircuitOpenException>();

        releaseNewProbe.TrySetResult();
        await Assert.That(await newProbe!).IsEqualTo(42);
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
    public async Task Reentrant_Failure_Is_Attributed_To_Its_Concurrent_Parent()
    {
        var monitor = new CircuitBreakerMonitor();
        var nestedFailure = new InvalidOperationException("nested observer");
        var firstObserverEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstObserver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockFirstIsolation = true;
        _ = Shield.CircuitBreaker(options =>
        {
            options.Monitor = monitor;
            options.OnStateChanged = change =>
            {
                if (change is { From: CircuitState.Closed, To: CircuitState.Isolated })
                {
                    if (blockFirstIsolation)
                    {
                        blockFirstIsolation = false;
                        firstObserverEntered.TrySetResult();
                        releaseFirstObserver.Task.GetAwaiter().GetResult();
                        return;
                    }

                    throw nestedFailure;
                }

                if (change.To == CircuitState.Closed)
                {
                    monitor.Isolate();
                }
            };
        });

        var first = Task.Run(monitor.Isolate);
        await firstObserverEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Task.Run(monitor.Reset);
        await WaitForStateAsync(monitor, CircuitState.Closed);

        releaseFirstObserver.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Isolated);
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

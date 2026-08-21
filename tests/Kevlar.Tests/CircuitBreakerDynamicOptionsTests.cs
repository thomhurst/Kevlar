using System.Collections.Concurrent;
using Kevlar.Internal;
using Kevlar.Strategies;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CircuitBreakerDynamicOptionsTests
{
    [Test]
    public async Task Default_BreakDuration_Event_Rejects_Missing_Context()
    {
        var item = default(CircuitBreakerBreakDurationEvent);

        await Assert.That(() => item.Context).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BreakDurationGenerator_Receives_Handled_Outcome_And_Context()
    {
        var timeProvider = new FakeTimeProvider();
        var durations = new Queue<TimeSpan>([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)]);
        var observed = new List<(object? Result, Exception? Exception, string? ShieldName)>();
        var shield = Shield.For<int>()
            .WhenResult(result => result < 0)
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDurationGenerator = item =>
                {
                    observed.Add((item.Result, item.Exception, item.Context.ShieldName));
                    return new ValueTask<TimeSpan>(durations.Dequeue());
                };
            })
            .WithName("dynamic-breaker")
            .WithTimeProvider(timeProvider);

        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(-1))).IsEqualTo(-1);
        var firstRejection = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        await Assert.That(firstRejection.Exception).IsTypeOf<CircuitOpenException>();
        await Assert.That(((CircuitOpenException)firstRejection.Exception!).RetryAfter).IsEqualTo(TimeSpan.FromSeconds(1));

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(-2))).IsEqualTo(-2);
        var secondRejection = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1));
        await Assert.That(((CircuitOpenException)secondRejection.Exception!).RetryAfter).IsEqualTo(TimeSpan.FromSeconds(3));

        await Assert.That(observed.Count).IsEqualTo(2);
        await Assert.That(observed[0].Result).IsEqualTo(-1);
        await Assert.That(observed[0].Exception).IsNull();
        await Assert.That(observed[0].ShieldName).IsEqualTo("dynamic-breaker");
        await Assert.That(observed[1].Result).IsEqualTo(-2);
    }

    [Test]
    public async Task BreakDurationGenerator_Runs_Outside_Lock_And_Discards_Stale_Result()
    {
        var monitor = new CircuitBreakerMonitor();
        var generatorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGenerator = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.Monitor = monitor;
            options.BreakDurationGenerator = async _ =>
            {
                generatorEntered.SetResult();
                monitor.Isolate();
                await releaseGenerator.Task;
                return TimeSpan.FromMinutes(1);
            };
        });

        var execution = shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException()).AsTask();
        await generatorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Isolated);

        releaseGenerator.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Isolated);
    }

    [Test]
    public async Task BreakDurationGenerator_Failure_Propagates_Exactly_And_Allows_Another_Trip()
    {
        var generatorFailure = new InvalidOperationException("generator");
        var calls = 0;
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.Monitor = monitor;
            options.BreakDurationGenerator = _ => ++calls == 1
                ? ValueTask.FromException<TimeSpan>(generatorFailure)
                : new ValueTask<TimeSpan>(TimeSpan.FromMinutes(1));
        });

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw new ApplicationException("operation")))
            .Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(thrown, generatorFailure)).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new ApplicationException("operation"));
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Synchronous_BreakDurationGenerator_Failure_Releases_The_Opening_Reservation()
    {
        var expected = new InvalidOperationException("synchronous generator");
        var calls = 0;
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.Monitor = monitor;
            options.BreakDurationGenerator = _ =>
            {
                if (++calls == 1)
                {
                    throw expected;
                }

                return new ValueTask<TimeSpan>(TimeSpan.FromMinutes(1));
            };
        });

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw new ApplicationException("operation")))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, expected)).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new ApplicationException("operation"));
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Generated_BreakDuration_Must_Be_Positive()
    {
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDurationGenerator = _ => new ValueTask<TimeSpan>(TimeSpan.Zero);
        });

        await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Asynchronously_Generated_BreakDuration_Must_Be_Positive()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.Monitor = monitor;
            options.BreakDurationGenerator = async _ =>
            {
                await Task.Yield();
                return TimeSpan.Zero;
            };
        });

        _ = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Caller_Cancellation_During_BreakDurationGenerator_Propagates_Exactly()
    {
        using var cancellation = new CancellationTokenSource();
        var generatorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGenerator = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.Monitor = monitor;
            options.BreakDurationGenerator = async item =>
            {
                generatorEntered.SetResult();
                await releaseGenerator.Task;
                item.Context.CancellationToken.ThrowIfCancellationRequested();
                return TimeSpan.FromMinutes(1);
            };
        });

        var execution = shield.ExecuteAsync<int>(
            _ => throw new InvalidOperationException("operation"),
            cancellation.Token).AsTask();
        await generatorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        releaseGenerator.SetResult();

        var thrown = await Assert.That(async () => await execution).Throws<OperationCanceledException>();
        await Assert.That(thrown!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Concurrent_Trip_Invokes_One_BreakDurationGenerator()
    {
        var generatorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGenerator = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.Monitor = monitor;
            options.BreakDurationGenerator = async _ =>
            {
                Interlocked.Increment(ref calls);
                generatorEntered.SetResult();
                await releaseGenerator.Task;
                return TimeSpan.FromMinutes(1);
            };
        });

        var executions = Enumerable.Range(0, 32)
            .Select(_ => shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException()).AsTask())
            .ToArray();
        await generatorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Yield();
        await Assert.That(calls).IsEqualTo(1);

        releaseGenerator.SetResult();
        await Task.WhenAll(executions).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Failure_During_Pending_Generator_Remains_In_Ratio_Window()
    {
        var timeProvider = new FakeTimeProvider();
        var generatorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGenerator = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generatorFailure = new InvalidOperationException("generator");
        var generatorCalls = 0;
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 0.5;
            options.MinimumThroughput = 2;
            options.SamplingWindow = TimeSpan.FromSeconds(10);
            options.Monitor = monitor;
            options.BreakDurationGenerator = async _ =>
            {
                if (Interlocked.Increment(ref generatorCalls) > 1)
                {
                    return TimeSpan.FromMinutes(1);
                }

                generatorEntered.SetResult();
                await releaseGenerator.Task;
                throw generatorFailure;
            };
        }).WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        var opening = shield.ExecuteOutcomeAsync<int>(
            _ => ValueTask.FromException<int>(new ApplicationException("first"))).AsTask();
        await generatorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        timeProvider.Advance(TimeSpan.FromSeconds(9));
        await shield.ExecuteOutcomeAsync<int>(
            _ => ValueTask.FromException<int>(new ApplicationException("pending")));
        releaseGenerator.SetResult();
        var openingOutcome = await opening;
        await Assert.That(ReferenceEquals(openingOutcome.Exception, generatorFailure)).IsTrue();

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        await shield.ExecuteAsync(_ => ValueTask.CompletedTask);
        await shield.ExecuteOutcomeAsync<int>(
            _ => ValueTask.FromException<int>(new ApplicationException("latest")));

        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Async_State_Callback_Is_Awaited_Before_Monitor_And_Serializes_Transitions()
    {
        var monitor = new CircuitBreakerMonitor();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<string>();
        var activeCallbacks = 0;
        var maximumConcurrentCallbacks = 0;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.Monitor = monitor;
            options.OnStateChanged = change => events.Enqueue($"sync:{change.To}");
            options.OnStateChangedAsync = async change =>
            {
                var active = Interlocked.Increment(ref activeCallbacks);
                InterlockedExtensions.Max(ref maximumConcurrentCallbacks, active);
                events.Enqueue($"async:{change.To}:start");
                if (change.To == CircuitState.Open)
                {
                    callbackEntered.SetResult();
                    await releaseCallback.Task;
                }

                events.Enqueue($"async:{change.To}:end");
                Interlocked.Decrement(ref activeCallbacks);
            };
        });
        monitor.StateChanged += change => events.Enqueue($"monitor:{change.To}");

        var opening = shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException()).AsTask();
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(opening.IsCompleted).IsFalse();

        var reset = monitor.ResetAsync().AsTask();
        await Assert.That(reset.IsCompleted).IsFalse();
        releaseCallback.SetResult();
        await Task.WhenAll(opening, reset).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(events.SequenceEqual(
        [
            "sync:Open",
            "async:Open:start",
            "async:Open:end",
            "monitor:Open",
            "sync:Closed",
            "async:Closed:start",
            "async:Closed:end",
            "monitor:Closed",
        ])).IsTrue();
        await Assert.That(maximumConcurrentCallbacks).IsEqualTo(1);
    }

    [Test]
    public async Task Async_State_Callback_Can_Await_Reentrant_Control()
    {
        var monitor = new CircuitBreakerMonitor();
        var observed = new ConcurrentQueue<CircuitState>();
        _ = Shield.CircuitBreaker(options =>
        {
            options.Monitor = monitor;
            options.OnStateChangedAsync = async change =>
            {
                observed.Enqueue(change.To);
                if (change.To == CircuitState.Isolated)
                {
                    await monitor.ResetAsync();
                }
            };
        });

        await monitor.IsolateAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(observed.SequenceEqual([CircuitState.Isolated, CircuitState.Closed])).IsTrue();
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Closed);
    }

    [Test]
    public async Task Async_State_Callback_Failure_Does_Not_Skip_Monitor_And_Preserves_Instance()
    {
        var callbackFailure = new InvalidOperationException("async callback");
        var monitor = new CircuitBreakerMonitor();
        CircuitState? observed = null;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.Monitor = monitor;
            options.OnStateChangedAsync = _ => ValueTask.FromException(callbackFailure);
        });
        monitor.StateChanged += change => observed = change.To;

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync<int>(_ => throw new ApplicationException("operation")))
            .Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(thrown, callbackFailure)).IsTrue();
        await Assert.That(observed).IsEqualTo(CircuitState.Open);
        await Assert.That(monitor.State).IsEqualTo(CircuitState.Open);
    }

    [Test]
    public async Task Configured_Breaker_Returns_Synchronous_Processing_Failure_As_Faulted_ValueTask()
    {
        var predicateFailure = new InvalidOperationException("predicate");
        var shield = Shield.When(_ => throw predicateFailure).CircuitBreaker(options =>
        {
            options.OnStateChangedAsync = static _ => ValueTask.CompletedTask;
        });
        var strategy = (CircuitBreakerStrategy)shield.Strategies.Single();
        var context = KevlarContext.Rent(default, isSynchronous: false, TimeProvider.System, shieldName: null);

        try
        {
            var continuation = new Continuation<int, object?>(
                next: null,
                static (_, _) => new ValueTask<Outcome<int>>(
                    Outcome<int>.FromException(new ApplicationException("operation"))),
                state: null);

            var execution = strategy.ExecuteAsync(continuation, context);

            await Assert.That(execution.IsCompletedSuccessfully).IsFalse();
            var thrown = await Assert.That(async () => await execution).Throws<InvalidOperationException>();
            await Assert.That(ReferenceEquals(thrown, predicateFailure)).IsTrue();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    [Test]
    public async Task Dynamic_Breaker_Releases_Probe_When_Synchronous_HalfOpen_Callback_Fails()
    {
        var timeProvider = new FakeTimeProvider();
        var callbackFailure = new InvalidOperationException("half-open callback");
        var failCallback = true;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDurationGenerator = static _ =>
                new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
            options.OnStateChanged = change =>
            {
                if (change.To == CircuitState.HalfOpen && failCallback)
                {
                    failCallback = false;
                    throw callbackFailure;
                }
            };
        }).WithTimeProvider(timeProvider);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new ApplicationException("open"));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, callbackFailure)).IsTrue();

        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(42))).IsEqualTo(42);
    }

    [Test]
    public async Task Dynamic_Breaker_Releases_Probe_When_Async_HalfOpen_Callback_Fails()
    {
        var timeProvider = new FakeTimeProvider();
        var expected = new InvalidOperationException("async half-open callback");
        var failCallback = true;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDurationGenerator = static _ =>
                new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
            options.OnStateChangedAsync = change =>
            {
                if (change.To == CircuitState.HalfOpen && failCallback)
                {
                    failCallback = false;
                    return ValueTask.FromException(expected);
                }

                return ValueTask.CompletedTask;
            };
        }).WithTimeProvider(timeProvider);

        await shield.ExecuteOutcomeAsync<int>(_ => throw new ApplicationException("open"));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var thrown = await Assert.That(async () =>
                await shield.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(thrown, expected)).IsTrue();
        await Assert.That(await shield.ExecuteAsync(_ => new ValueTask<int>(42))).IsEqualTo(42);
    }

    [Test]
    public async Task Dynamic_Breaker_Description_Does_Not_Execute_Generator()
    {
        var calls = 0;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 2;
            options.BreakDurationGenerator = _ =>
            {
                calls++;
                return new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
            };
        });

        await Assert.That(shield.ToString()).IsEqualTo("CircuitBreaker(2 consecutive, break dynamic)");
        await Assert.That(calls).IsEqualTo(0);
    }
}

file static class InterlockedExtensions
{
    public static void Max(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}

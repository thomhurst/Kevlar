namespace Kevlar.Tests;

/// <summary>Defines synchronization and execution-context behavior across strategy boundaries.</summary>
public class AmbientContextContractTests
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>Verifies retry continuations do not post to the caller synchronization context.</summary>
    [Test]
    public async Task Retry_Does_Not_Post_Internal_Continuations_To_The_Caller_Context()
    {
        var synchronizationContext = new NonPumpingSynchronizationContext();
        var firstAttempt = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new List<Observation>();
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ =>
            {
                Record(observations, "retry");
                return default;
            };
        });

        Ambient.Value = "retry-flow";
        var execution = StartUnderContext(synchronizationContext, () => shield.ExecuteAsync<int>(_ =>
        {
            Record(observations, $"action-{++attempts}");
            return attempts == 1 ? new ValueTask<int>(firstAttempt.Task) : new ValueTask<int>(42);
        }).AsTask());

        firstAttempt.SetException(new InvalidOperationException("retry"));

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(synchronizationContext.PostCount).IsEqualTo(0);
        await Assert.That(observations.Select(item => item.Name)).IsEquivalentTo(
            ["action-1", "retry", "action-2"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(observations.All(item => item.Ambient == "retry-flow")).IsTrue();
        await Assert.That(observations[0].SynchronizationContext).IsSameReferenceAs(synchronizationContext);
        await Assert.That(observations.Skip(1).All(item => item.SynchronizationContext is null)).IsTrue();
        Ambient.Value = null;
    }

    /// <summary>Verifies timeout continuations do not post to the caller synchronization context.</summary>
    [Test]
    public async Task Timeout_Does_Not_Post_Internal_Continuations_To_The_Caller_Context()
    {
        var synchronizationContext = new NonPumpingSynchronizationContext();
        var timeProvider = new ControlledTimeProvider();
        var observations = new List<Observation>();
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ =>
            {
                Record(observations, "timeout");
                return default;
            };
        }).WithTimeProvider(timeProvider);

        Ambient.Value = "timeout-flow";
        var execution = StartUnderContext(synchronizationContext, () => shield.ExecuteOutcomeAsync<int>(token =>
        {
            Record(observations, "action");
            return WaitForCancellationAsync(token);
        }).AsTask());

        await timeProvider.WaitForTimersAsync(1);
        timeProvider.FireTimer(0);
        var outcome = await execution;

        await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(synchronizationContext.PostCount).IsEqualTo(0);
        await Assert.That(observations.Select(item => item.Name)).IsEquivalentTo(
            ["action", "timeout"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(observations.All(item => item.Ambient == "timeout-flow")).IsTrue();
        await Assert.That(observations[0].SynchronizationContext).IsSameReferenceAs(synchronizationContext);
        await Assert.That(observations[1].SynchronizationContext).IsNull();
        Ambient.Value = null;
    }

    /// <summary>Verifies fallback continuations do not post to the caller synchronization context.</summary>
    [Test]
    public async Task Fallback_Does_Not_Post_Internal_Continuations_To_The_Caller_Context()
    {
        var synchronizationContext = new NonPumpingSynchronizationContext();
        var failure = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new List<Observation>();
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Fallback(
                _ =>
                {
                    Record(observations, "fallback");
                    return new ValueTask<int>(42);
                },
                options => options.OnFallback = _ =>
                {
                    Record(observations, "on-fallback");
                    return default;
                });

        Ambient.Value = "fallback-flow";
        var execution = StartUnderContext(synchronizationContext, () => shield.ExecuteAsync(_ =>
        {
            Record(observations, "action");
            return new ValueTask<int>(failure.Task);
        }).AsTask());

        failure.SetException(new InvalidOperationException("fallback"));

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(synchronizationContext.PostCount).IsEqualTo(0);
        await Assert.That(observations.Select(item => item.Name)).IsEquivalentTo(
            ["action", "on-fallback", "fallback"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(observations.All(item => item.Ambient == "fallback-flow")).IsTrue();
        await Assert.That(observations[0].SynchronizationContext).IsSameReferenceAs(synchronizationContext);
        await Assert.That(observations.Skip(1).All(item => item.SynchronizationContext is null)).IsTrue();
        Ambient.Value = null;
    }

    /// <summary>Verifies queued limiter work does not post to the caller synchronization context.</summary>
    [Test]
    public async Task Queued_Limiter_Does_Not_Post_Internal_Continuations_To_The_Caller_Context()
    {
        var synchronizationContext = new NonPumpingSynchronizationContext();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runningStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new List<Observation>();
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1, queueLimit: 1);

        var running = shield.ExecuteAsync(async _ =>
        {
            runningStarted.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await runningStarted.Task;

        Ambient.Value = "limiter-flow";
        var queued = StartUnderContext(synchronizationContext, () => shield.ExecuteAsync(_ =>
        {
            Record(observations, "queued-action");
            return new ValueTask<int>(2);
        }).AsTask());

        release.SetResult();

        await Assert.That(await running).IsEqualTo(1);
        await Assert.That(await queued).IsEqualTo(2);
        await Assert.That(synchronizationContext.PostCount).IsEqualTo(0);
        await Assert.That(observations.Single().Ambient).IsEqualTo("limiter-flow");
        await Assert.That(observations.Single().SynchronizationContext).IsNull();
        Ambient.Value = null;
    }

    /// <summary>Verifies strategy callbacks receive the caller execution context.</summary>
    [Test]
    public async Task Retry_Fallback_Timeout_And_Hedge_Callbacks_Observe_ExecutionContext()
    {
        var observations = new List<Observation>();
        Ambient.Value = "callback-flow";

        var retry = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ =>
            {
                Record(observations, "retry");
                return default;
            };
        });
        var retryCalls = 0;
        await retry.ExecuteOutcomeAsync<int>(_ =>
        {
            retryCalls++;
            return retryCalls == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(1);
        });

        var fallback = Shield.For<int>()
            .When<InvalidOperationException>()
            .Fallback(
                _ =>
                {
                    Record(observations, "fallback-action");
                    return new ValueTask<int>(2);
                },
                options => options.OnFallback = _ =>
                {
                    Record(observations, "fallback-callback");
                    return default;
                });
        await fallback.ExecuteAsync<int>(_ => throw new InvalidOperationException());

        var timeProvider = new ControlledTimeProvider();
        var timeout = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ =>
            {
                Record(observations, "timeout");
                return default;
            };
        }).WithTimeProvider(timeProvider);
        var timed = timeout.ExecuteOutcomeAsync<int>(WaitForCancellationAsync).AsTask();
        await timeProvider.WaitForTimersAsync(1);
        timeProvider.FireTimer(0);
        await timed;

        var hedge = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ =>
            {
                Record(observations, "hedge");
                return default;
            };
        });
        var hedgeCalls = 0;
        await hedge.ExecuteAsync(token => Interlocked.Increment(ref hedgeCalls) == 1
            ? WaitForCancellationAsync(token)
            : new ValueTask<int>(3));

        await Assert.That(observations.Select(item => item.Name)).IsEquivalentTo(
            ["retry", "fallback-callback", "fallback-action", "timeout", "hedge"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(observations.All(item => item.Ambient == "callback-flow")).IsTrue();
        Ambient.Value = null;
    }

    /// <summary>Verifies hedge attempts isolate their ambient-context mutations.</summary>
    [Test]
    public async Task Hedge_Attempts_Isolate_AsyncLocal_Mutations_From_Each_Other_And_Later_Executions()
    {
        var before = new string?[2];
        var after = new string?[2];
        var attemptsStarted = new AsyncCounter("hedge attempts with ambient context");
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptNumber = 0;
        var shield = Shield.Hedge(1, TimeSpan.Zero);
        Ambient.Value = "caller";

        var execution = shield.ExecuteAsync<int>(async token =>
        {
            var current = Interlocked.Increment(ref attemptNumber);
            before[current - 1] = Ambient.Value;
            Ambient.Value = $"attempt-{current}";
            after[current - 1] = Ambient.Value;
            attemptsStarted.Signal();

            if (current == 1)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                return -1;
            }

            await releaseSecond.Task;
            return 42;
        }).AsTask();

        await attemptsStarted.WaitForAsync(2);
        await Assert.That(before[0]).IsEqualTo("caller");
        await Assert.That(before[1]).IsEqualTo("caller");
        await Assert.That(after[0]).IsEqualTo("attempt-1");
        await Assert.That(after[1]).IsEqualTo("attempt-2");
        await Assert.That(Ambient.Value).IsEqualTo("caller");

        releaseSecond.SetResult();
        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(Ambient.Value).IsEqualTo("caller");

        Ambient.Value = "later";
        var laterObserved = await shield.ExecuteAsync(_ => new ValueTask<string?>(Ambient.Value));
        await Assert.That(laterObserved).IsEqualTo("later");
        Ambient.Value = null;
    }

    /// <summary>Verifies suppressed execution context remains suppressed in actions and callbacks.</summary>
    [Test]
    public async Task Suppressed_ExecutionContext_Does_Not_Flow_Into_Action_Or_Callback()
    {
        Ambient.Value = "must-not-flow";
        Task<(string? Action, string? Callback)> execution;

        using (ExecutionContext.SuppressFlow())
        {
            execution = Task.Run(async () =>
            {
                string? actionValue = null;
                string? callbackValue = null;
                var calls = 0;
                var shield = Shield.Retry(options =>
                {
                    options.MaxRetries = 1;
                    options.Backoff = Backoff.None;
                    options.OnRetry = _ =>
                    {
                        callbackValue = Ambient.Value;
                        return default;
                    };
                });

                await shield.ExecuteAsync<int>(_ =>
                {
                    actionValue = Ambient.Value;
                    return Interlocked.Increment(ref calls) == 1
                        ? ValueTask.FromException<int>(new InvalidOperationException())
                        : new ValueTask<int>(42);
                });

                return (actionValue, callbackValue);
            });
        }

        var observed = await execution;
        await Assert.That(observed.Action).IsNull();
        await Assert.That(observed.Callback).IsNull();
        await Assert.That(Ambient.Value).IsEqualTo("must-not-flow");
        Ambient.Value = null;
    }

    /// <summary>Verifies synchronous delays block without pumping a synchronization context.</summary>
    [Test]
    public async Task Synchronous_Delayed_Execution_Blocks_Without_Pumping_SynchronizationContext()
    {
        var synchronizationContext = new NonPumpingSynchronizationContext();
        var timeProvider = new ControlledTimeProvider();
        var attempts = 0;
        var shield = Shield.Retry(1, Backoff.Constant(TimeSpan.FromSeconds(1)))
            .WithTimeProvider(timeProvider);

        var execution = Task.Factory.StartNew(
            () => StartUnderContext(synchronizationContext, () => Task.FromResult(shield.Execute<int>(_ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("retry");
                }

                return 42;
            }))).GetAwaiter().GetResult(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        await timeProvider.WaitForTimersAsync(1);
        timeProvider.FireTimer(0);

        await Assert.That(await execution.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(synchronizationContext.PostCount).IsEqualTo(0);
    }

    private static Task<T> StartUnderContext<T>(SynchronizationContext synchronizationContext, Func<Task<T>> start)
    {
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        try
        {
            return start();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prior);
        }
    }

    private static ValueTask<int> WaitForCancellationAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(static state =>
        {
            var (source, cancellationToken) = ((TaskCompletionSource<int>, CancellationToken))state!;
            source.TrySetCanceled(cancellationToken);
        }, (completion, token));
        return new ValueTask<int>(completion.Task);
    }

    private static void Record(List<Observation> observations, string name)
    {
        lock (observations)
        {
            observations.Add(new Observation(name, Ambient.Value, SynchronizationContext.Current));
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
        }
    }

    private sealed record Observation(
        string Name,
        string? Ambient,
        SynchronizationContext? SynchronizationContext);
}

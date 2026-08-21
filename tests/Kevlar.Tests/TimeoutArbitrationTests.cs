namespace Kevlar.Tests;

public class TimeoutArbitrationTests
{
    [Test]
    public async Task Caller_Cancellation_Wins_After_Timeout_Fires()
    {
        var timeProvider = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = 0;
        CancellationToken executionToken = default;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            executionToken = token;
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }, callerCancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        callerCancellation.Cancel();
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(executionToken).IsNotEqualTo(callerCancellation.Token);
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken == callerCancellation.Token)
            .IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Caller_Cancellation_Wins_Before_Timeout_Fires()
    {
        var timeProvider = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = 0;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }, callerCancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callerCancellation.Cancel();
        timeProvider.Fire(0);
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken == callerCancellation.Token)
            .IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Equal_Nested_Timeouts_Report_Only_The_Outer_Timeout()
    {
        var timeProvider = new ManualTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outerEvents = 0;
        var innerEvents = 0;
        var shield = Shield
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ => outerEvents++;
            })
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ => innerEvents++;
            })
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(1);
        timeProvider.Fire(0);
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(outerEvents).IsEqualTo(1);
        await Assert.That(innerEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Unrelated_Cancellation_After_Timer_Fires_Is_Not_Reclassified()
    {
        var timeProvider = new ManualTimeProvider();
        using var unrelatedCancellation = new CancellationTokenSource();
        var unrelated = new OperationCanceledException("unrelated", unrelatedCancellation.Token);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = 0;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async _ =>
        {
            started.SetResult();
            await release.Task;
            throw unrelated;
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        release.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(ReferenceEquals(outcome.Exception, unrelated)).IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Success_After_Timeout_Token_Cancellation_Is_Delivered()
    {
        var timeProvider = new ManualTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = 0;
        CancellationToken executionToken = default;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(1);
            options.OnTimeout = _ => timeoutEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(async token =>
        {
            executionToken = token;
            started.SetResult();
            await release.Task;
            return 42;
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        release.SetResult();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(executionToken.IsCancellationRequested).IsTrue();
        await Assert.That(timeoutEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Outer_Strategy_Sees_Caller_Token_After_Success_And_Timeout()
    {
        var timeProvider = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var observer = new TokenObserverStrategy();
        var shield = Shield.Use(observer)
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(timeProvider);

        var success = await shield.ExecuteAsync(_ => new ValueTask<int>(42), callerCancellation.Token);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedExecution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }, callerCancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(1);
        release.SetResult();
        var timedOutcome = await timedExecution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(success).IsEqualTo(42);
        await Assert.That(timedOutcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(observer.Observations.Count).IsEqualTo(2);
        foreach (var observation in observer.Observations)
        {
            await Assert.That(observation.Before).IsEqualTo(callerCancellation.Token);
            await Assert.That(observation.After).IsEqualTo(callerCancellation.Token);
        }
    }

    [Test]
    public async Task OnTimeout_Failure_Leaves_Context_Clean_And_Next_Execution_Healthy()
    {
        var timeProvider = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var callbackFailure = new ApplicationException("timeout callback failed");
        var callbackCalls = 0;
        var observer = new TokenObserverStrategy();
        var shield = Shield.Use(observer)
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ =>
                {
                    callbackCalls++;
                    throw callbackFailure;
                };
            })
            .WithTimeProvider(timeProvider);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstExecution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            started.SetResult();
            await release.Task;
            throw new OperationCanceledException(token);
        }, callerCancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        release.SetResult();
        var failed = await firstExecution.WaitAsync(TimeSpan.FromSeconds(5));
        var recovered = await shield.ExecuteAsync(_ => new ValueTask<int>(42), callerCancellation.Token);

        await Assert.That(ReferenceEquals(failed.Exception, callbackFailure)).IsTrue();
        await Assert.That(recovered).IsEqualTo(42);
        await Assert.That(callbackCalls).IsEqualTo(1);
        await Assert.That(observer.Observations.Count).IsEqualTo(2);
        foreach (var observation in observer.Observations)
        {
            await Assert.That(observation.Before).IsEqualTo(callerCancellation.Token);
            await Assert.That(observation.After).IsEqualTo(callerCancellation.Token);
        }
    }

    [Test]
    public async Task Queued_Stale_Timer_Cannot_Cancel_A_Later_Execution()
    {
        var timeProvider = new ManualTimeProvider();
        var shield = Shield.Timeout(TimeSpan.FromSeconds(1)).WithTimeProvider(timeProvider);

        await shield.ExecuteAsync(_ => new ValueTask<int>(1));

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken secondToken = default;
        var secondExecution = shield.ExecuteAsync(async token =>
        {
            secondToken = token;
            started.SetResult();
            await release.Task;
            token.ThrowIfCancellationRequested();
            return 2;
        }).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Fire(0);
        await Assert.That(secondToken.IsCancellationRequested).IsFalse();
        release.SetResult();

        await Assert.That(await secondExecution.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(2);
    }

    private sealed class TokenObserverStrategy : Strategy
    {
        public List<TokenObservation> Observations { get; } = [];

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var before = context.CancellationToken;
            try
            {
                return await next.InvokeAsync(context);
            }
            finally
            {
                Observations.Add(new TokenObservation(before, context.CancellationToken));
            }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }

        public void Fire(int timerIndex) => _timers[timerIndex].Fire();

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => default;

            public void Fire() => callback(state);
        }
    }

    private sealed record TokenObservation(CancellationToken Before, CancellationToken After);
}

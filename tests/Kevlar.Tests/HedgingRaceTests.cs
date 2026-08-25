using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class HedgingRaceTests
{
    [Test]
    public async Task Caller_Cancellation_During_Finite_Stagger_Does_Not_Launch_Hedges()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var hedgeEvents = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.Delay = TimeSpan.FromSeconds(1);
            options.OnHedge = _ => hedgeEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            Interlocked.Increment(ref attempts);
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(hedgeEvents).IsEqualTo(0);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Caller_Cancellation_During_Infinite_Stagger_Does_Not_Launch_Hedges()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var hedgeEvents = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
            options.OnHedge = _ => hedgeEvents++;
        });

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            Interlocked.Increment(ref attempts);
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(hedgeEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Timer_Before_Caller_Cancellation_Launches_Only_One_Hedge()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var hedgeEvents = 0;
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.Delay = TimeSpan.FromSeconds(1);
            options.OnHedge = _ => hedgeEvents++;
        }).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            if (Interlocked.Increment(ref attempts) == 2)
            {
                secondStarted.TrySetResult();
            }

            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(hedgeEvents).IsEqualTo(1);
    }

    [Test]
    public async Task Simultaneous_Acceptable_Outcomes_Complete_Both_Attempts_Safely()
    {
        var releases = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var completions = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.Hedge(1, TimeSpan.Zero);

        var execution = shield.ExecuteAsync(async _ =>
        {
            var attempt = Interlocked.Increment(ref attempts) - 1;
            if (attempts == 2)
            {
                allStarted.TrySetResult();
            }

            try
            {
                await releases[attempt].Task;
                return attempt + 1;
            }
            finally
            {
                completions[attempt].TrySetResult();
            }
        }).AsTask();

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releases[0].SetResult();
        releases[1].SetResult();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(completions.Select(completion => completion.Task)).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result is 1 or 2).IsTrue();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public Task Unhandled_Exception_Wins_When_Released_First() =>
        RunUnhandledExceptionAndSuccessRace(unhandledExceptionWins: true);

    [Test]
    public Task Success_Wins_When_Released_First() =>
        RunUnhandledExceptionAndSuccessRace(unhandledExceptionWins: false);

    [Test]
    public async Task Attempts_Mutate_Isolated_Context_Properties()
    {
        var key = new KevlarKey<int>("attempt");
        var parentObserver = new ParentPropertyObserver(key);
        var forkObserver = new ForkPropertyObserver(key, participantCount: 2);
        var shield = Shield
            .Use(parentObserver)
            .Hedge(1, TimeSpan.Zero)
            .Use(forkObserver);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));
        await forkObserver.WaitForAllObservedAsync();

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(forkObserver.ObservedValues.Count).IsEqualTo(2);
        await Assert.That(forkObserver.ObservedValues[1]).IsEqualTo(1);
        await Assert.That(forkObserver.ObservedValues[2]).IsEqualTo(2);
        await Assert.That(parentObserver.Before).IsEqualTo(-1);
        await Assert.That(parentObserver.After).IsEqualTo(-1);
    }

    [Test]
    public async Task OnHedge_Failure_Cancels_Primary_And_Next_Execution_Is_Healthy()
    {
        var callbackFailure = new ApplicationException("hedge callback failed");
        var callbackCalls = 0;
        var primaryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primaryCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ =>
            {
                if (Interlocked.Increment(ref callbackCalls) == 1)
                {
                    throw callbackFailure;
                }
            };
        });

        var failed = await shield.ExecuteOutcomeAsync<int>(async token =>
        {
            Interlocked.Increment(ref attempts);
            using var registration = token.Register(() => primaryCancelled.TrySetResult());
            primaryStarted.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        });

        await primaryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await primaryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var recovered = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(ReferenceEquals(failed.Exception, callbackFailure)).IsTrue();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(recovered).IsEqualTo(42);
    }

    [Test]
    public async Task OnHedge_Caller_Cancellation_Does_Not_Invoke_The_Next_Attempt()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var hedgeEvents = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ =>
            {
                hedgeEvents++;
                cancellation.Cancel();
            };
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(async token =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token);

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(hedgeEvents).IsEqualTo(1);
    }

    [Test]
    public async Task Repeated_Late_Loser_Failures_Do_Not_Contaminate_Pooled_State()
    {
        var shield = Shield.Hedge(1, TimeSpan.Zero);

        for (var iteration = 0; iteration < 32; iteration++)
        {
            var releaseLoser = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loserCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = 0;

            var result = await shield.ExecuteAsync(async _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    try
                    {
                        await releaseLoser.Task;
                        throw new ApplicationException($"late loser {iteration}");
                    }
                    finally
                    {
                        loserCompleted.TrySetResult();
                    }
                }

                return iteration;
            });

            releaseLoser.SetResult();
            await loserCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(result).IsEqualTo(iteration);
            await Assert.That(attempts).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Concurrent_Executions_Of_One_Shield_Isolate_Attempts()
    {
        var shield = Shield.Hedge(1, TimeSpan.Zero);
        var executions = Enumerable.Range(0, 64).Select(async executionId =>
        {
            var attempts = 0;
            var primaryCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = await shield.ExecuteAsync(async token =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    using var registration = token.Register(() => primaryCancelled.TrySetResult());
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                }

                return executionId;
            });

            await primaryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return (result, attempts);
        });

        var results = await Task.WhenAll(executions).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(results.Select(result => result.result))
            .IsEquivalentTo(Enumerable.Range(0, 64));
        await Assert.That(results.All(result => result.attempts == 2)).IsTrue();
    }

    private static async Task RunUnhandledExceptionAndSuccessRace(bool unhandledExceptionWins)
    {
        var releases = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var unhandled = new ArgumentException("unhandled");
        var shield = Shield.When<InvalidOperationException>().Hedge(1, TimeSpan.Zero);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            var attempt = Interlocked.Increment(ref attempts) - 1;
            if (attempts == 2)
            {
                allStarted.TrySetResult();
            }

            await releases[attempt].Task.WaitAsync(token);
            if (attempt == 0)
            {
                throw unhandled;
            }

            return 42;
        }).AsTask();

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releases[unhandledExceptionWins ? 0 : 1].SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        if (unhandledExceptionWins)
        {
            await Assert.That(ReferenceEquals(outcome.Exception, unhandled)).IsTrue();
        }
        else
        {
            await Assert.That(outcome.Result).IsEqualTo(42);
        }
    }

    private sealed class ForkPropertyObserver(KevlarKey<int> key, int participantCount) : Strategy
    {
        private readonly TaskCompletionSource _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        private int _observed;

        public System.Collections.Concurrent.ConcurrentDictionary<int, int> ObservedValues { get; } = new();

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var attempt = Interlocked.Increment(ref _arrived);
            context.Properties.Set(key, attempt);
            if (attempt == participantCount)
            {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ObservedValues[attempt] = context.Properties.GetOrDefault(key, -1);
            if (Interlocked.Increment(ref _observed) == participantCount)
            {
                _allObserved.TrySetResult();
            }

            return await next.InvokeAsync(context);
        }

        public Task WaitForAllObservedAsync() =>
            _allObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ParentPropertyObserver(KevlarKey<int> key) : Strategy
    {
        public int Before { get; private set; }

        public int After { get; private set; }

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Before = context.Properties.GetOrDefault(key, -1);
            try
            {
                return await next.InvokeAsync(context);
            }
            finally
            {
                After = context.Properties.GetOrDefault(key, -1);
            }
        }
    }
}

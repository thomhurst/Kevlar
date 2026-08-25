namespace Kevlar.Tests;

public class RetryHedgeTimeoutCompositionTests
{
    [Test]
    public async Task Retry_Outside_Hedge_Starts_A_New_Group_After_Group_Exhaustion()
    {
        var events = new List<string>();
        var invocations = 0;
        var shield = Shield
            .When<InvalidOperationException>()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = _ => events.Add("retry-group");
            })
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 2;
                options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
                options.OnHedge = hedge => events.Add($"hedge-{hedge.AttemptNumber}");
            });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            events.Add($"action-{++invocations}");
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(events).IsEquivalentTo(
            [
                "action-1", "hedge-2", "action-2", "hedge-3", "action-3",
                "retry-group",
                "action-4", "hedge-2", "action-5", "hedge-3", "action-6",
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Hedge_Outside_Retry_Gives_Each_Attempt_Its_Own_Retry_Loop()
    {
        var events = new List<string>();
        var invocations = 0;
        var shield = Shield
            .When<InvalidOperationException>()
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 2;
                options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
                options.OnHedge = hedge => events.Add($"hedge-{hedge.AttemptNumber}");
            })
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = _ => events.Add("attempt-retry");
            });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            events.Add($"action-{++invocations}");
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(events).IsEquivalentTo(
            [
                "action-1", "attempt-retry", "action-2",
                "hedge-2", "action-3", "attempt-retry", "action-4",
                "hedge-3", "action-5", "attempt-retry", "action-6",
            ],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Retry_And_Hedge_Multiply_Handled_Result_Attempts()
    {
        var invocations = 0;
        var shield = Shield.For<int>()
            .WhenResult(-1)
            .Retry(1, Backoff.None)
            .Hedge(2, System.Threading.Timeout.InfiniteTimeSpan);

        var result = await shield.ExecuteAsync(_ =>
            new ValueTask<int>(Interlocked.Increment(ref invocations) == 6 ? 42 : -1));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(invocations).IsEqualTo(6);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Winner_Stops_Outer_Retries_And_Loser_Retry_Loops(bool retryOutside)
    {
        var invocations = 0;
        var retryEvents = 0;
        var loserCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = CreateRetryHedge(retryOutside, () => retryEvents++);

        var result = await shield.ExecuteAsync<int>(async token =>
        {
            if (Interlocked.Increment(ref invocations) == 2)
            {
                return 42;
            }

            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                return -1;
            }
            catch (OperationCanceledException)
            {
                loserCancelled.TrySetResult();
                throw;
            }
        });

        await loserCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(invocations).IsEqualTo(2);
        await Assert.That(retryEvents).IsEqualTo(0);
    }

    [Test]
    public async Task Unhandled_Exception_And_Caller_Cancellation_Do_Not_Multiply_Work()
    {
        var invocations = 0;
        var shield = Shield
            .When<InvalidOperationException>()
            .Retry(2, Backoff.None)
            .Hedge(2, System.Threading.Timeout.InfiniteTimeSpan);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref invocations);
            throw new ArgumentException("unhandled");
        })).Throws<ArgumentException>();
        await Assert.That(invocations).IsEqualTo(1);

        using var cancellation = new CancellationTokenSource();
        invocations = 0;
        var cancelled = await shield.ExecuteOutcomeAsync<int>(token =>
        {
            Interlocked.Increment(ref invocations);
            cancellation.Cancel();
            throw new OperationCanceledException(token);
        }, cancellation.Token);

        await Assert.That(cancelled.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)cancelled.Exception!).CancellationToken == cancellation.Token)
            .IsTrue();
        await Assert.That(invocations).IsEqualTo(1);
    }

    [Test]
    public async Task Total_Timeout_Outside_Hedge_Cancels_All_Forks_And_Prevents_Launches()
    {
        var timeProvider = new ControlledTimeProvider();
        var attempts = new AsyncCounter("outer-timeout hedge attempts");
        var hedgeEvents = 0;
        var timeoutEvents = 0;
        var shield = Shield
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ => timeoutEvents++;
            })
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 2;
                options.Delay = TimeSpan.FromSeconds(10);
                options.OnHedge = _ => hedgeEvents++;
            })
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            attempts.Signal();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 42;
        }).AsTask();

        await attempts.WaitForAsync(1);
        await timeProvider.WaitForTimersAsync(2);
        timeProvider.FireTimer(0);
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(attempts.Count).IsEqualTo(1);
        await Assert.That(hedgeEvents).IsEqualTo(0);
        await Assert.That(timeoutEvents).IsEqualTo(1);
    }

    [Test]
    public async Task Timeout_Inside_Hedge_Gives_Attempts_Independent_Budgets()
    {
        var timeProvider = new ControlledTimeProvider();
        var attempts = new AsyncCounter("per-attempt timeout hedge attempts");
        var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutEvents = new AsyncCounter("per-attempt timeout events");
        var attemptNumber = 0;
        var shield = Shield
            .When<TimeoutExceededException>()
            .Hedge(1, TimeSpan.Zero)
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ => timeoutEvents.Signal();
            })
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync<int>(async token =>
        {
            var current = Interlocked.Increment(ref attemptNumber);
            attempts.Signal();
            if (current == 1)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                return -1;
            }

            await secondRelease.Task;
            return 42;
        }).AsTask();

        await attempts.WaitForAsync(2);
        await timeProvider.WaitForTimersAsync(2);
        timeProvider.FireTimer(0);
        await timeoutEvents.WaitForAsync(1);
        secondRelease.TrySetResult();

        await Assert.That(await execution.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(42);
        await Assert.That(timeoutEvents.Count).IsEqualTo(1);
        await Assert.That(timeProvider.IsTimerDisposed(1)).IsTrue();
    }

    private static Shield CreateRetryHedge(bool retryOutside, Action onRetry)
    {
        var shield = Shield.When<InvalidOperationException>();
        return retryOutside
            ? shield
                .Retry(options =>
                {
                    options.MaxRetries = 2;
                    options.Backoff = Backoff.None;
                    options.OnRetry = _ => onRetry();
                })
                .Hedge(1, TimeSpan.Zero)
            : shield
                .Hedge(1, TimeSpan.Zero)
                .Retry(options =>
                {
                    options.MaxRetries = 2;
                    options.Backoff = Backoff.None;
                    options.OnRetry = _ => onRetry();
                });
    }
}

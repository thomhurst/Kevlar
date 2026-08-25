using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class RetryHookCancellationTests
{
    [Test]
    public async Task DelayGenerator_Cancellation_Stops_The_Next_Attempt_After_Hooks()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var synchronousHooks = 0;
        var asynchronousHooks = 0;
        var failure = new InvalidOperationException("original failure");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ =>
            {
                cancellation.Cancel();
                return TimeSpan.Zero;
            };
            options.OnRetry = _ => synchronousHooks++;
            options.OnRetryAsync = _ =>
            {
                asynchronousHooks++;
                return default;
            };
        });

        var outcome = await shield.ExecuteOutcomeAsync(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(failure);
        }, cancellation.Token);

        await AssertCancellationAsync(outcome, cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(synchronousHooks).IsEqualTo(1);
        await Assert.That(asynchronousHooks).IsEqualTo(1);
    }

    [Test]
    public async Task Typed_OnRetry_Cancellation_Stops_Next_Task_Action_After_Async_Hook()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var asynchronousHooks = 0;
        var seenResult = 0;
        var shield = Shield.For<int>()
            .WhenResult(-1)
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    seenResult = retry.Outcome.Result;
                    cancellation.Cancel();
                };
                options.OnRetryAsync = _ =>
                {
                    asynchronousHooks++;
                    return default;
                };
            });

        var outcome = await shield.ExecuteOutcomeAsync(_ =>
        {
            attempts++;
            return Task.FromResult(-1);
        }, cancellation.Token);

        await AssertCancellationAsync(outcome, cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(seenResult).IsEqualTo(-1);
        await Assert.That(asynchronousHooks).IsEqualTo(1);
    }

    [Test]
    public async Task Cancellation_While_OnRetryAsync_Runs_Stops_The_Next_Attempt()
    {
        using var cancellation = new CancellationTokenSource();
        var hookStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var failure = new InvalidOperationException("original failure");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.OnRetryAsync = async _ =>
            {
                hookStarted.SetResult();
                await releaseHook.Task;
            };
        });

        var execution = shield.ExecuteOutcomeAsync(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(failure);
        }, cancellation.Token).AsTask();

        await hookStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        releaseHook.SetResult();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await AssertCancellationAsync(outcome, cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task OnRetry_Cancellation_Stops_Synchronous_Execution()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var failure = new InvalidOperationException("original failure");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => cancellation.Cancel();
        });

        await Assert.That(() => shield.Execute<int>(_ =>
        {
            attempts++;
            throw failure;
        }, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task DelayGenerator_Failure_Skips_Later_Hooks_And_Attempts()
    {
        var callbackFailure = new FormatException("delay generator failed");
        var attempts = 0;
        var laterHooks = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ => throw callbackFailure;
            options.OnRetry = _ => laterHooks++;
            options.OnRetryAsync = _ =>
            {
                laterHooks++;
                return default;
            };
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(ReferenceEquals(outcome.Exception, callbackFailure)).IsTrue();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(laterHooks).IsEqualTo(0);
    }

    [Test]
    public async Task OnRetry_Failure_Does_Not_Skip_Async_Hook_Or_Attempts()
    {
        var callbackFailure = new FormatException("sync hook failed");
        var attempts = 0;
        var asynchronousHooks = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => throw callbackFailure;
            options.OnRetryAsync = _ =>
            {
                asynchronousHooks++;
                return default;
            };
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(4);
        await Assert.That(asynchronousHooks).IsEqualTo(3);
    }

    [Test]
    public async Task OnRetryAsync_Failure_Does_Not_Replace_The_Action_Failure()
    {
        var callbackFailure = new FormatException("async hook failed");
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.OnRetryAsync = _ => ValueTask.FromException(callbackFailure);
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(4);
    }

    [Test]
    public async Task OnRetryAsync_Cancellation_Does_Not_Replace_The_Action_Failure()
    {
        using var callbackCancellation = new CancellationTokenSource();
        callbackCancellation.Cancel();
        var cancellationFailure = new OperationCanceledException(callbackCancellation.Token);
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.OnRetryAsync = _ => ValueTask.FromException(cancellationFailure);
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(4);
    }

    [Test]
    public async Task Hooks_Run_In_Order_With_Exception_Event_Data()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("retry me");
        var order = new List<string>();
        var events = new List<(int RetryNumber, TimeSpan Delay, Exception? Exception, object? Result, CancellationToken Token)>();
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = retry =>
            {
                order.Add("delay");
                events.Add((retry.RetryNumber, retry.Delay, retry.Exception, retry.Result, retry.Context.CancellationToken));
                return TimeSpan.Zero;
            };
            options.OnRetry = retry =>
            {
                order.Add("sync");
                events.Add((retry.RetryNumber, retry.Delay, retry.Exception, retry.Result, retry.Context.CancellationToken));
            };
            options.OnRetryAsync = retry =>
            {
                order.Add("async");
                events.Add((retry.RetryNumber, retry.Delay, retry.Exception, retry.Result, retry.Context.CancellationToken));
                return default;
            };
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            order.Add($"attempt-{attempts}");
            return attempts == 1
                ? ValueTask.FromException<int>(failure)
                : new ValueTask<int>(42);
        }, cancellation.Token);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(string.Join(",", order)).IsEqualTo("attempt-1,delay,sync,async,attempt-2");
        await Assert.That(events.Count).IsEqualTo(3);
        foreach (var retryEvent in events)
        {
            await Assert.That(retryEvent.RetryNumber).IsEqualTo(1);
            await Assert.That(retryEvent.Delay).IsEqualTo(TimeSpan.Zero);
            await Assert.That(ReferenceEquals(retryEvent.Exception, failure)).IsTrue();
            await Assert.That(retryEvent.Result).IsNull();
            await Assert.That(retryEvent.Token).IsEqualTo(cancellation.Token);
        }
    }

    [Test]
    public async Task Typed_Hooks_Run_In_Order_With_Result_Event_Data()
    {
        var order = new List<string>();
        var results = new List<int>();
        var attempts = 0;
        var shield = Shield.For<int>()
            .WhenResult(-1)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.DelayGenerator = retry =>
                {
                    order.Add("delay");
                    results.Add(retry.Outcome.Result);
                    return TimeSpan.Zero;
                };
                options.OnRetry = retry =>
                {
                    order.Add("sync");
                    results.Add(retry.Outcome.Result);
                };
                options.OnRetryAsync = retry =>
                {
                    order.Add("async");
                    results.Add(retry.Outcome.Result);
                    return default;
                };
            });

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            order.Add($"attempt-{attempts}");
            return Task.FromResult(attempts == 1 ? -1 : 42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(string.Join(",", order)).IsEqualTo("attempt-1,delay,sync,async,attempt-2");
        await Assert.That(results).IsEquivalentTo([-1, -1, -1]);
    }

    [Test]
    public async Task Cancellation_Before_Backoff_Surfaces_Caller_Cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("handled failure");
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.Constant(TimeSpan.FromHours(1));
            options.OnRetry = _ => cancellation.Cancel();
        });

        var outcome = await shield.ExecuteOutcomeAsync(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(failure);
        }, cancellation.Token);

        await AssertCancellationAsync(outcome, cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Cancellation_During_Backoff_Surfaces_Caller_Cancellation()
    {
        var fakeTime = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.Retry(3, Backoff.Constant(TimeSpan.FromHours(1)))
            .WithTimeProvider(fakeTime);

        var execution = shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        }, cancellation.Token).AsTask();

        await Assert.That(attempts).IsEqualTo(1);
        cancellation.Cancel();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
    }

    private static async Task AssertCancellationAsync<T>(Outcome<T> outcome, CancellationToken expectedToken)
    {
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(expectedToken);
    }
}

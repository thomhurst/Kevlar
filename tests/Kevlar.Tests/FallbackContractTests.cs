using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class FallbackContractTests
{
    [Test]
    public async Task OnFallback_Failure_Does_Not_Skip_Recovery()
    {
        var callbackFailure = new ApplicationException("callback failed");
        var callbackCalls = 0;
        var factoryCalls = 0;
        var shield = Shield.For<int>().Fallback(
            (_, _) =>
            {
                factoryCalls++;
                return new ValueTask<int>(42);
            },
            options => options.OnFallback = _ =>
            {
                callbackCalls++;
                if (callbackCalls == 1)
                {
                    throw callbackFailure;
                }

                return default;
            });

        var failed = await shield.ExecuteOutcomeAsync<int>(_ =>
            throw new InvalidOperationException("original"));
        var recovered = await shield.ExecuteAsync<int>(_ =>
            throw new InvalidOperationException("original"));

        await Assert.That(failed.Result).IsEqualTo(42);
        await Assert.That(recovered).IsEqualTo(42);
        await Assert.That(callbackCalls).IsEqualTo(2);
        await Assert.That(factoryCalls).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_Async_Fallback_Observes_Caller_Cancellation_And_Preserves_Failure()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallbackFailure = new OperationCanceledException("fallback cancelled", cancellation.Token);
        CancellationToken seenToken = default;
        var sawCancellation = false;
        var shield = Shield.For<int>().Fallback(async token =>
        {
            seenToken = token;
            started.SetResult();
            await release.Task;
            sawCancellation = token.IsCancellationRequested;
            throw fallbackFailure;
        });

        var execution = shield.ExecuteAsync<int>(_ =>
            throw new InvalidOperationException("original"), cancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        release.SetResult();
        var caught = await CaptureCancellationAsync(execution);

        await Assert.That(seenToken).IsEqualTo(cancellation.Token);
        await Assert.That(sawCancellation).IsTrue();
        await Assert.That(ReferenceEquals(caught, fallbackFailure)).IsTrue();
    }

    [Test]
    public async Task Void_Async_Fallback_Observes_Caller_Cancellation_And_Preserves_Failure()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallbackFailure = new OperationCanceledException("void fallback cancelled", cancellation.Token);
        CancellationToken seenToken = default;
        var sawCancellation = false;
        var shield = Shield.Empty.Fallback(async (_, token) =>
        {
            seenToken = token;
            started.SetResult();
            await release.Task;
            sawCancellation = token.IsCancellationRequested;
            throw fallbackFailure;
        });

        var execution = shield.ExecuteAsync(_ =>
            throw new InvalidOperationException("original"), cancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        release.SetResult();
        var caught = await CaptureCancellationAsync(execution);

        await Assert.That(seenToken).IsEqualTo(cancellation.Token);
        await Assert.That(sawCancellation).IsTrue();
        await Assert.That(ReferenceEquals(caught, fallbackFailure)).IsTrue();
    }

    [Test]
    public async Task OperationCanceledException_Is_Bypassed_By_Default_And_Handleable_Explicitly()
    {
        using var cancellation = new CancellationTokenSource();
        var original = new OperationCanceledException("action cancelled", cancellation.Token);
        var defaultFactoryCalls = 0;
        var explicitFactoryCalls = 0;
        Exception? observed = null;
        var defaultShield = Shield.For<int>().Fallback(_ =>
        {
            defaultFactoryCalls++;
            return new ValueTask<int>(42);
        });
        var explicitShield = Shield.For<int>()
            .When<OperationCanceledException>()
            .Fallback(
                _ =>
                {
                    explicitFactoryCalls++;
                    return new ValueTask<int>(42);
                },
                options => options.OnFallback = fallback =>
                {
                    observed = fallback.Outcome.Exception;
                    return default;
                });

        var bypassed = await defaultShield.ExecuteOutcomeAsync<int>(_ => throw original);
        var recovered = await explicitShield.ExecuteAsync<int>(_ => throw original);

        await Assert.That(ReferenceEquals(bypassed.Exception, original)).IsTrue();
        await Assert.That(recovered).IsEqualTo(42);
        await Assert.That(ReferenceEquals(observed, original)).IsTrue();
        await Assert.That(defaultFactoryCalls).IsEqualTo(0);
        await Assert.That(explicitFactoryCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Caller_Cancellation_Is_Not_Handled_By_Explicit_Exception_Fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallbackCalls = 0;
        var notificationCalls = 0;
        var shield = Shield.For<int>()
            .When<Exception>()
            .Fallback(
                (_, _) =>
                {
                    fallbackCalls++;
                    return new ValueTask<int>(-1);
                },
                options => options.OnFallback = _ =>
                {
                    notificationCalls++;
                    return default;
                });

        var execution = shield.ExecuteAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var caught = await CaptureCancellationAsync(execution);

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(fallbackCalls).IsEqualTo(0);
        await Assert.That(notificationCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Fallback_Outside_Timeout_Receives_Restored_Caller_Token()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var actionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken fallbackToken = default;
        Outcome<int> handledOutcome = default;
        Outcome<int> eventOutcome = default;
        var shield = Shield.For<int>()
            .When<TimeoutExceededException>()
            .Fallback((outcome, token) =>
            {
                handledOutcome = outcome;
                fallbackToken = token;
                return new ValueTask<int>(42);
            }, options => options.OnFallback = fallback =>
            {
                eventOutcome = fallback.Outcome;
                return default;
            })
            .Timeout(TimeSpan.FromSeconds(1))
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(async token =>
        {
            actionStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        }, cancellation.Token).AsTask();

        await actionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(fallbackToken).IsEqualTo(cancellation.Token);
        await Assert.That(fallbackToken.IsCancellationRequested).IsFalse();
        await Assert.That(handledOutcome.Exception).IsTypeOf<TimeoutExceededException>();
        await Assert.That(ReferenceEquals(eventOutcome.Exception, handledOutcome.Exception)).IsTrue();
    }

    [Test]
    public async Task Timeout_Outside_Slow_Fallback_Cancels_The_Fallback()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var fallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken fallbackToken = default;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Timeout(TimeSpan.FromSeconds(1))
            .Fallback(async token =>
            {
                fallbackToken = token;
                fallbackStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 42;
            })
            .WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync<int>(_ =>
            throw new InvalidOperationException("original"), cancellation.Token).AsTask();

        await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var failure = await CaptureFailureAsync(execution);

        await Assert.That(failure).IsTypeOf<TimeoutExceededException>();
        await Assert.That(((TimeoutExceededException)failure!).Timeout).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(fallbackToken).IsNotEqualTo(cancellation.Token);
        await Assert.That(fallbackToken.IsCancellationRequested).IsTrue();
        await Assert.That(cancellation.IsCancellationRequested).IsFalse();
    }

    [Test]
    public async Task Fallback_Outside_Exhausted_Hedge_Fires_Exactly_Once()
    {
        var original = new InvalidOperationException("all attempts failed");
        var attempts = 0;
        var eventCalls = 0;
        var factoryCalls = 0;
        Exception? observed = null;
        var shield = Shield.For<int>()
            .Fallback(
                (outcome, _) =>
                {
                    factoryCalls++;
                    observed = outcome.Exception;
                    return new ValueTask<int>(42);
                },
                options => options.OnFallback = _ =>
                {
                    eventCalls++;
                    return default;
                })
            .Hedge(2, Timeout.InfiniteTimeSpan);

        var result = await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw original;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(eventCalls).IsEqualTo(1);
        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(ReferenceEquals(observed, original)).IsTrue();
    }

    [Test]
    public async Task Fallback_Inside_Hedge_With_The_Same_Clause_Is_Rejected()
    {
        await Assert.That(() =>
        {
            _ = Shield.For<int>().Hedge(1, TimeSpan.Zero).FallbackTo(-1);
        }).Throws<InvalidOperationException>();
        await Assert.That(() =>
        {
            _ = Shield.Hedge(1, TimeSpan.Zero).Fallback((_, _) => default);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Event_Precedes_Factory_And_Preserves_Exception_Outcome()
    {
        var original = new InvalidOperationException("original");
        var order = new List<string>();
        Outcome<int> eventOutcome = default;
        Outcome<int> factoryOutcome = default;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Fallback(
                (outcome, _) =>
                {
                    order.Add("factory");
                    factoryOutcome = outcome;
                    return new ValueTask<int>(42);
                },
                options => options.OnFallback = fallback =>
                {
                    order.Add("event");
                    eventOutcome = fallback.Outcome;
                    return default;
                });

        var result = await shield.ExecuteAsync<int>(_ => throw original);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(string.Join(",", order)).IsEqualTo("event,factory");
        await Assert.That(ReferenceEquals(eventOutcome.Exception, original)).IsTrue();
        await Assert.That(ReferenceEquals(factoryOutcome.Exception, original)).IsTrue();
    }

    [Test]
    public async Task Event_Precedes_Factory_And_Preserves_Result_Outcome()
    {
        var order = new List<string>();
        Outcome<int> eventOutcome = default;
        Outcome<int> factoryOutcome = default;
        var shield = Shield.For<int>()
            .WhenResultEquals(-1)
            .Fallback(
                (outcome, _) =>
                {
                    order.Add("factory");
                    factoryOutcome = outcome;
                    return new ValueTask<int>(42);
                },
                options => options.OnFallback = fallback =>
                {
                    order.Add("event");
                    eventOutcome = fallback.Outcome;
                    return default;
                });

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(string.Join(",", order)).IsEqualTo("event,factory");
        await Assert.That(eventOutcome.Result).IsEqualTo(-1);
        await Assert.That(factoryOutcome.Result).IsEqualTo(-1);
    }

    [Test]
    public async Task Sync_Async_And_Outcome_Boundaries_Are_Equivalent()
    {
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .FallbackTo(42);

        var synchronous = shield.Execute(_ => throw new InvalidOperationException());
        var asynchronous = await shield.ExecuteAsync<int>(_ =>
            throw new InvalidOperationException());
        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
            throw new InvalidOperationException());

        await Assert.That(synchronous).IsEqualTo(42);
        await Assert.That(asynchronous).IsEqualTo(42);
        await Assert.That(outcome.IsSuccess).IsTrue();
        await Assert.That(outcome.Result).IsEqualTo(42);
    }

    private static async Task<OperationCanceledException?> CaptureCancellationAsync(Task execution)
    {
        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            return null;
        }
        catch (OperationCanceledException exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> CaptureFailureAsync(Task execution)
    {
        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}

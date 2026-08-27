namespace Kevlar.Tests;

public class FallbackAsyncHookTests
{
    [Test]
    public async Task Typed_Hook_Is_Awaited_Before_Recovery_With_The_Exact_Result()
    {
        var handled = new object();
        var recovered = new object();
        var order = new List<string>();
        Outcome<object> hookOutcome = default;
        Outcome<object> factoryOutcome = default;
        var shield = Shield.For<object>()
            .WhenResultEquals(handled)
            .Fallback(
                (outcome, _) =>
                {
                    order.Add("factory");
                    factoryOutcome = outcome;
                    return new ValueTask<object>(recovered);
                },
                options =>
                {
                    options.OnFallback = async fallback =>
                    {
                        order.Add("hook:start");
                        hookOutcome = fallback.Outcome;
                        await Task.Yield();
                        order.Add("hook:end");
                    };
                });

        var result = await shield.ExecuteAsync(_ => new ValueTask<object>(handled));

        await Assert.That(ReferenceEquals(result, recovered)).IsTrue();
        await Assert.That(string.Join(",", order)).IsEqualTo("hook:start,hook:end,factory");
        await Assert.That(ReferenceEquals(hookOutcome.Result, handled)).IsTrue();
        await Assert.That(ReferenceEquals(factoryOutcome.Result, handled)).IsTrue();
    }

    [Test]
    public async Task Truly_Asynchronous_Hook_Is_Awaited_Before_Recovery()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var shield = Shield.For<int>().Fallback(
            _ =>
            {
                factoryCalls++;
                return new ValueTask<int>(42);
            },
            options =>
            {
                options.OnFallback = async _ =>
                {
                    started.SetResult();
                    await release.Task;
                };
            });

        var execution = shield.ExecuteAsync(_ => throw new InvalidOperationException()).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(factoryCalls).IsEqualTo(0);

        release.SetResult();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(factoryCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Async_Hook_Failure_Does_Not_Skip_Recovery()
    {
        var original = new InvalidOperationException("original");
        var hookFailure = new ApplicationException("hook failed");
        Exception? observed = null;
        var factoryCalls = 0;
        var shield = Shield.For<int>().Fallback(
            _ =>
            {
                factoryCalls++;
                return new ValueTask<int>(42);
            },
            options =>
            {
                options.OnFallback = fallback =>
                {
                    observed = fallback.Outcome.Exception;
                    return ValueTask.FromException(hookFailure);
                };
            });

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ => throw original);

        await Assert.That(ReferenceEquals(observed, original)).IsTrue();
        await Assert.That(outcome.Result).IsEqualTo(42);
        await Assert.That(factoryCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Async_Hook_Cancellation_Does_Not_Skip_Recovery()
    {
        using var cancellation = new CancellationTokenSource();
        var hookCancellation = new OperationCanceledException("hook cancelled", cancellation.Token);
        var shield = Shield.For<int>().FallbackTo(
            42,
            options => options.OnFallback = _ => ValueTask.FromException(hookCancellation));

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(outcome.Result).IsEqualTo(42);
    }

    [Test]
    public async Task Caller_Cancellation_Remains_Observable_Without_Forcing_Recovery_To_Stop()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken hookToken = default;
        CancellationToken factoryToken = default;
        var shield = Shield.For<int>().Fallback(
            (_, token) =>
            {
                factoryToken = token;
                return new ValueTask<int>(42);
            },
            options =>
            {
                options.OnFallback = async fallback =>
                {
                    started.SetResult();
                    await release.Task;
                    hookToken = fallback.Context.CancellationToken;
                };
            });

        var execution = shield.ExecuteAsync<int>(_ => throw new InvalidOperationException(), cancellation.Token).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        release.SetResult();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(hookToken).IsEqualTo(cancellation.Token);
        await Assert.That(hookToken.IsCancellationRequested).IsTrue();
        await Assert.That(factoryToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Async_Hooks_Are_Reentrant_And_Concurrent()
    {
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var nestedResult = 0;
        Shield<int>? shield = null;
        shield = Shield<int>.Empty
            .WithName("fallback-hook")
            .FallbackTo(
                42,
                options =>
                {
                    options.OnFallback = async fallback =>
                    {
                        await Task.Yield();
                        nestedResult = await shield!.ExecuteAsync(_ => new ValueTask<int>(7));
                        await Assert.That(fallback.Context.ShieldName).IsEqualTo("fallback-hook");
                        if (Interlocked.Increment(ref started) == 2)
                        {
                            bothStarted.SetResult();
                        }

                        await release.Task;
                    };
                });

        var first = shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()).AsTask();
        var second = shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()).AsTask();

        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(results).IsEquivalentTo([42, 42]);
        await Assert.That(nestedResult).IsEqualTo(7);
    }

    [Test]
    public async Task Untyped_Hook_Is_Awaited_Before_Recovery_And_Preserves_Exception_Identity()
    {
        var original = new InvalidOperationException("original");
        var order = new List<string>();
        Exception? hookException = null;
        var shield = Shield.Empty.Fallback(
            (exception, _) =>
            {
                order.Add("fallback");
                return ValueTask.CompletedTask;
            },
            options =>
            {
                options.OnFallback = async fallback =>
                {
                    order.Add("hook:start");
                    hookException = fallback.Exception;
                    await Task.Yield();
                    order.Add("hook:end");
                };
            });

        await shield.ExecuteAsync(_ => throw original);

        await Assert.That(string.Join(",", order)).IsEqualTo("hook:start,hook:end,fallback");
        await Assert.That(ReferenceEquals(hookException, original)).IsTrue();
    }

    [Test]
    public async Task Untyped_Async_Hook_Failure_Does_Not_Skip_Recovery()
    {
        var hookFailure = new ApplicationException("hook failed");
        var fallbackCalls = 0;
        var shield = Shield
            .When<InvalidOperationException>()
            .Fallback(
                (_, _) =>
                {
                    fallbackCalls++;
                    return ValueTask.CompletedTask;
                },
                options => options.OnFallback = _ => ValueTask.FromException(hookFailure));

        await shield.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(fallbackCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Untyped_CancellationToken_Overload_Configures_Notifications()
    {
        var notificationCalls = 0;
        var fallbackCalls = 0;
        var shield = Shield.Empty.Fallback(
            _ =>
            {
                fallbackCalls++;
                return ValueTask.CompletedTask;
            },
            options => options.OnFallback = _ =>
            {
                notificationCalls++;
                return default;
            });

        await shield.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(notificationCalls).IsEqualTo(1);
        await Assert.That(fallbackCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Notification_Configuration_Runs_Once_When_The_Shield_Is_Built()
    {
        var configureCalls = 0;
        var firstCalls = 0;
        var shield = Shield.For<int>().FallbackTo(
            42,
            options =>
            {
                configureCalls++;
                options.OnFallback = _ =>
                {
                    firstCalls++;
                    return ValueTask.CompletedTask;
                };
            });

        var firstResult = await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException());
        var secondResult = await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException());

        await Assert.That(firstResult).IsEqualTo(42);
        await Assert.That(secondResult).IsEqualTo(42);
        await Assert.That(configureCalls).IsEqualTo(1);
        await Assert.That(firstCalls).IsEqualTo(2);
    }
}

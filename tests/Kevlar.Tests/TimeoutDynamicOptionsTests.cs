using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class TimeoutDynamicOptionsTests
{
    [Test]
    public async Task Generated_Timeout_Uses_The_Execution_Context()
    {
        var fakeTime = new FakeTimeProvider();
        string? observedName = null;
        var shield = Shield.Timeout(options =>
            {
                options.TimeoutGenerator = context =>
                {
                    observedName = context.ShieldName;
                    return new ValueTask<TimeSpan>(TimeSpan.FromSeconds(2));
                };
            })
            .WithName("dynamic-timeout")
            .WithTimeProvider(fakeTime);

        var execution = shield.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(2));

        var exception = await Assert.That(async () => await execution).Throws<TimeoutExceededException>();
        await Assert.That(exception!.Timeout).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(observedName).IsEqualTo("dynamic-timeout");
        await Assert.That(shield.ToString()).IsEqualTo("dynamic-timeout: Timeout(dynamic)");
    }

    [Test]
    public async Task Generated_Timeout_Supports_Typed_Synchronous_Execution()
    {
        var sawSynchronousContext = false;
        var shield = Shield<int>.Empty.Timeout(options =>
        {
            options.TimeoutGenerator = context =>
            {
                sawSynchronousContext = context.IsSynchronous;
                return new ValueTask<TimeSpan>(TimeSpan.FromMinutes(1));
            };
        });

        var result = shield.Execute(_ => 42);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(sawSynchronousContext).IsTrue();
    }

    [Test]
    public async Task Async_Timeout_Generator_Is_Awaited_Before_Execution()
    {
        var generatorGate = new AsyncGate("timeout generator");
        var actionStarted = false;
        var shield = Shield.Timeout(options =>
        {
            options.TimeoutGenerator = async context =>
            {
                await generatorGate.EnterAsync(context.CancellationToken);
                return TimeSpan.FromMinutes(1);
            };
        });

        var execution = shield.ExecuteAsync(_ =>
        {
            actionStarted = true;
            return new ValueTask<int>(42);
        }).AsTask();

        await generatorGate.WaitForEntryAsync();
        await Assert.That(actionStarted).IsFalse();
        await Assert.That(execution.IsCompleted).IsFalse();

        generatorGate.Release();

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(actionStarted).IsTrue();
    }

    [Test]
    public async Task Caller_Cancellation_During_Generation_Skips_Execution()
    {
        using var callerCancellation = new CancellationTokenSource();
        var generatorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actionStarted = false;
        var shield = Shield.Timeout(options =>
        {
            options.TimeoutGenerator = async context =>
            {
                generatorStarted.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, context.CancellationToken);
                return TimeSpan.FromMinutes(1);
            };
        });

        var execution = shield.ExecuteOutcomeAsync(
            _ =>
            {
                actionStarted = true;
                return new ValueTask<int>(42);
            },
            callerCancellation.Token).AsTask();

        await generatorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callerCancellation.Cancel();
        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(actionStarted).IsFalse();
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(callerCancellation.Token);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Invalid_Generated_Timeout_Fails_Before_Execution(int seconds)
    {
        var actionStarted = false;
        var shield = Shield.Timeout(options =>
        {
            options.TimeoutGenerator = _ => new ValueTask<TimeSpan>(TimeSpan.FromSeconds(seconds));
        });

        var outcome = await shield.ExecuteOutcomeAsync(_ =>
        {
            actionStarted = true;
            return new ValueTask<int>(42);
        });

        await Assert.That(actionStarted).IsFalse();
        await Assert.That(outcome.Exception).IsTypeOf<KevlarConfigurationException>();
    }

    [Test]
    public async Task Async_Timeout_Hook_Is_Ordered_After_Synchronous_Hook_And_Awaited()
    {
        var fakeTime = new FakeTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        var hookGate = new AsyncGate("async timeout hook");
        var observations = new List<string>();
        CancellationToken callbackToken = default;
        var shield = Shield.Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeout = _ => observations.Add("sync");
                options.OnTimeoutAsync = async timeout =>
                {
                    callbackToken = timeout.Context.CancellationToken;
                    observations.Add("async-start");
                    await hookGate.EnterAsync();
                    observations.Add("async-end");
                };
            })
            .WithTimeProvider(fakeTime);

        var execution = shield.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, callerCancellation.Token).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await hookGate.WaitForEntryAsync();

        await Assert.That(execution.IsCompleted).IsFalse();
        await Assert.That(observations).IsEquivalentTo(["sync", "async-start"]);
        await Assert.That(callbackToken).IsEqualTo(callerCancellation.Token);

        hookGate.Release();

        await Assert.That(async () => await execution).Throws<TimeoutExceededException>();
        await Assert.That(observations).IsEquivalentTo(["sync", "async-start", "async-end"]);
    }

    [Test]
    public async Task Async_Timeout_Hook_Failure_Surfaces_The_Exact_Exception()
    {
        var fakeTime = new FakeTimeProvider();
        var callbackFailure = new ApplicationException("async timeout callback failed");
        var shield = Shield.Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeoutAsync = _ => ValueTask.FromException(callbackFailure);
            })
            .WithTimeProvider(fakeTime);

        var execution = shield.ExecuteOutcomeAsync<int>(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        var outcome = await execution;

        await Assert.That(ReferenceEquals(outcome.Exception, callbackFailure)).IsTrue();
    }

    [Test]
    public async Task Synchronously_Completed_Async_Hook_Receives_Generated_Timeout()
    {
        var fakeTime = new FakeTimeProvider();
        var observed = TimeSpan.Zero;
        var shield = Shield.Timeout(options =>
            {
                options.TimeoutGenerator = _ =>
                    new ValueTask<TimeSpan>(TimeSpan.FromSeconds(1));
                options.OnTimeoutAsync = timeout =>
                {
                    observed = timeout.Timeout;
                    return ValueTask.CompletedTask;
                };
            })
            .WithTimeProvider(fakeTime);

        var execution = StartTimedExecution(shield);
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(async () => await execution).Throws<TimeoutExceededException>();
        await Assert.That(observed).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Synchronous_Execution_Waits_For_The_Async_Timeout_Hook()
    {
        var hookCompleted = false;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(10);
            options.OnTimeoutAsync = async _ =>
            {
                await Task.Yield();
                hookCompleted = true;
            };
        });

        await Assert.That(() => shield.Execute(token =>
        {
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            return 1;
        })).Throws<TimeoutExceededException>();
        await Assert.That(hookCompleted).IsTrue();
    }

    [Test]
    public async Task Timeout_Generator_Failure_Surfaces_The_Exact_Exception()
    {
        var generatorFailure = new ApplicationException("timeout generator failed");
        var actionStarted = false;
        var shield = Shield.Timeout(options =>
            options.TimeoutGenerator = _ => ValueTask.FromException<TimeSpan>(generatorFailure));

        var outcome = await shield.ExecuteOutcomeAsync(_ =>
        {
            actionStarted = true;
            return new ValueTask<int>(42);
        });

        await Assert.That(actionStarted).IsFalse();
        await Assert.That(ReferenceEquals(outcome.Exception, generatorFailure)).IsTrue();
    }

    [Test]
    public async Task Async_Timeout_Hooks_May_Run_Concurrently()
    {
        var fakeTime = new FakeTimeProvider();
        var hooksStarted = new AsyncCounter("concurrent timeout hooks");
        var releaseHooks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Timeout(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(1);
                options.OnTimeoutAsync = async _ =>
                {
                    hooksStarted.Signal();
                    await releaseHooks.Task;
                };
            })
            .WithTimeProvider(fakeTime);

        var first = StartTimedExecution(shield);
        var second = StartTimedExecution(shield);

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await hooksStarted.WaitForAsync(2);
        await Assert.That(first.IsCompleted).IsFalse();
        await Assert.That(second.IsCompleted).IsFalse();

        releaseHooks.SetResult();

        await Assert.That(async () => await first).Throws<TimeoutExceededException>();
        await Assert.That(async () => await second).Throws<TimeoutExceededException>();
    }

    private static Task<int> StartTimedExecution(Shield shield) =>
        shield.ExecuteAsync(async token =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();
}

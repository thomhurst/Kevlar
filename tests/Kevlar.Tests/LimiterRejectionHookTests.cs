using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class LimiterRejectionHookTests
{
    [Test]
    public async Task Rate_Limit_Hooks_Are_Ordered_And_Receive_Metadata()
    {
        var fakeTime = new FakeTimeProvider();
        var order = new List<string>();
        RateLimitRejectedEvent observed = default;
        string? observedShieldName = null;
        var observedStrategyIndex = -1;
        var shield = Shield.For<int>()
            .RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromSeconds(2);
                options.Burst = 1;
                options.QueueLimit = 0;
                options.OnRejected = rejection =>
                {
                    observed = rejection;
                    observedShieldName = rejection.Context.ShieldName;
                    observedStrategyIndex = rejection.Context.StrategyIndex;
                    order.Add("sync");
                };
                options.OnRejectedAsync = async rejection =>
                {
                    await Task.Yield();
                    await Assert.That(ReferenceEquals(rejection.Context, observed.Context)).IsTrue();
                    order.Add("async");
                };
            })
            .WithName("orders")
            .WithTimeProvider(fakeTime);

        await shield.ExecuteAsync(static _ => new ValueTask<int>(1));
        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(2));

        await Assert.That(outcome.Exception).IsTypeOf<RateLimitExceededException>();
        await Assert.That(order.SequenceEqual(["sync", "async"])).IsTrue();
        await Assert.That(observed.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(observed.Permits).IsEqualTo(1);
        await Assert.That(observed.Window).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(observed.Burst).IsEqualTo(1);
        await Assert.That(observed.QueueLimit).IsEqualTo(0);
        await Assert.That(observedStrategyIndex).IsEqualTo(0);
        await Assert.That(observedShieldName).IsEqualTo("orders");
    }

    [Test]
    public async Task Concurrency_Limit_Hooks_Are_Ordered_And_Receive_Metadata()
    {
        var order = new List<string>();
        ConcurrencyLimitRejectedEvent observed = default;
        string? observedShieldName = null;
        var observedStrategyIndex = -1;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield
            .When<InvalidOperationException>()
            .ConcurrencyLimit(options =>
            {
                options.MaxConcurrency = 1;
                options.QueueLimit = 0;
                options.OnRejected = rejection =>
                {
                    observed = rejection;
                    observedShieldName = rejection.Context.ShieldName;
                    observedStrategyIndex = rejection.Context.StrategyIndex;
                    order.Add("sync");
                };
                options.OnRejectedAsync = async rejection =>
                {
                    await Task.Yield();
                    await Assert.That(ReferenceEquals(rejection.Context, observed.Context)).IsTrue();
                    order.Add("async");
                };
            })
            .WithName("bulkhead");

        var running = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await started.Task;

        await Assert.That(async () => await shield.ExecuteAsync(static _ => new ValueTask<int>(2)))
            .Throws<ConcurrencyLimitExceededException>();

        await Assert.That(order.SequenceEqual(["sync", "async"])).IsTrue();
        await Assert.That(observed.MaxConcurrency).IsEqualTo(1);
        await Assert.That(observed.QueueLimit).IsEqualTo(0);
        await Assert.That(observedStrategyIndex).IsEqualTo(0);
        await Assert.That(observedShieldName).IsEqualTo("bulkhead");

        release.SetResult();
        await running;
    }

    [Test]
    public async Task Synchronous_Execution_Rejects_An_Async_Rejection_Hook()
    {
        var asyncHooks = 0;
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromMinutes(1);
            options.OnRejectedAsync = _ =>
            {
                asyncHooks++;
                return ValueTask.CompletedTask;
            };
        });

        var exception = await Assert.That(() => shield.Execute(static _ => 1))
            .Throws<NotSupportedException>();

        await Assert.That(exception!.Message).Contains("RateLimitOptions.OnRejectedAsync");
        await Assert.That(asyncHooks).IsEqualTo(0);
    }

    [Test]
    public async Task Synchronous_Rejection_Hook_Failure_Does_Not_Skip_Async_Hook()
    {
        var callbackFailure = new InvalidOperationException("sync rejection callback failed");
        var asyncHooks = 0;
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromMinutes(1);
            options.OnRejected = _ => throw callbackFailure;
            options.OnRejectedAsync = _ =>
            {
                asyncHooks++;
                return ValueTask.CompletedTask;
            };
        });

        await shield.ExecuteAsync(static _ => new ValueTask<int>(1));
        await Assert.That(async () => await shield.ExecuteAsync(static _ => new ValueTask<int>(2)))
            .Throws<RateLimitExceededException>();

        await Assert.That(asyncHooks).IsEqualTo(1);
    }

    [Test]
    public async Task Asynchronous_Rejection_Hook_Failure_Preserves_Rejection()
    {
        var callbackFailure = new InvalidOperationException("async rejection callback failed");
        var shield = Shield.ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = 1;
            options.OnRejectedAsync = _ => ValueTask.FromException(callbackFailure);
        });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await started.Task;

        var outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(2));

        await Assert.That(outcome.Exception).IsTypeOf<ConcurrencyLimitExceededException>();
        release.SetResult();
        await running;
    }

    [Test]
    public async Task Queued_Cancellation_Does_Not_Invoke_Rejection_Hooks()
    {
        var fakeTime = new FakeTimeProvider();
        var rejections = 0;
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromSeconds(1);
            options.QueueLimit = 1;
            options.OnRejected = _ => rejections++;
        }).WithTimeProvider(fakeTime);
        using var cancellation = new CancellationTokenSource();

        await shield.ExecuteAsync(static _ => new ValueTask<int>(0));
        var queued = shield.ExecuteAsync(static _ => new ValueTask<int>(1), cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        await Assert.That(rejections).IsEqualTo(0);

        var replacement = shield.ExecuteAsync(static _ => new ValueTask<int>(2)).AsTask();
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(await replacement.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(2);
        await Assert.That(rejections).IsEqualTo(0);
    }

    [Test]
    public async Task Concurrency_Queue_Cancellation_Does_Not_Invoke_Rejection_Hooks()
    {
        var rejections = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = 1;
            options.QueueLimit = 1;
            options.OnRejected = _ => rejections++;
        });
        using var cancellation = new CancellationTokenSource();
        var running = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
            return 1;
        }).AsTask();
        await started.Task;
        var queued = shield.ExecuteAsync(static _ => new ValueTask<int>(2), cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.That(async () => await queued).Throws<OperationCanceledException>();
        await Assert.That(rejections).IsEqualTo(0);

        var replacement = shield.ExecuteAsync(static _ => new ValueTask<int>(3)).AsTask();
        release.SetResult();
        await Assert.That(await running).IsEqualTo(1);
        await Assert.That(await replacement).IsEqualTo(3);
        await Assert.That(rejections).IsEqualTo(0);
    }
}

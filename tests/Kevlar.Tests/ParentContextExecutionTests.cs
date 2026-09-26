using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

[NotInParallel]
public class ParentContextExecutionTests
{
    private static readonly KevlarKey<string> RequestId = new("request-id");
    private static readonly KevlarKey<int> ChildValue = new("child-value");

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Empty_Nested_Execution_Keeps_Both_Property_Bags_Empty(bool synchronous)
    {
        await Shield.Empty.ExecuteWithContextAsync(async parent =>
        {
            var result = synchronous
                ? Shield.Empty.ExecuteWithContext(parent, static child => child.Properties.Count)
                : await Shield.Empty.ExecuteWithContextAsync(
                    parent, static child => new ValueTask<int>(child.Properties.Count));

            await Assert.That(result).IsEqualTo(0);
            await Assert.That(parent.Properties.Count).IsEqualTo(0);
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Empty_Parent_Receives_New_Child_Properties(bool synchronous)
    {
        await Shield.Empty.ExecuteWithContextAsync(async parent =>
        {
            if (synchronous)
            {
                Shield.Empty.ExecuteWithContext(parent, static child => child.Properties.Set(ChildValue, 42));
            }
            else
            {
                await Shield.Empty.ExecuteWithContextAsync(parent, static child =>
                {
                    child.Properties.Set(ChildValue, 42);
                    return ValueTask.CompletedTask;
                });
            }

            await Assert.That(parent.Properties.GetOrDefault(ChildValue)).IsEqualTo(42);
        });
    }

    [Test]
    public async Task Empty_Baseline_Preserves_Parent_Writes_While_Child_Is_Running()
    {
        await Shield.Empty.ExecuteWithContextAsync(async parent =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var childTask = Shield.Empty.ExecuteWithContextAsync(parent, async child =>
            {
                child.Properties.Set(ChildValue, 42);
                child.Properties.Set(RequestId, "child");
                entered.SetResult();
                await release.Task;
            });

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            parent.Properties.Set(RequestId, "parent");
            release.SetResult();
            await childTask;

            await Assert.That(parent.Properties.GetOrDefault(RequestId)).IsEqualTo("parent");
            await Assert.That(parent.Properties.GetOrDefault(ChildValue)).IsEqualTo(42);
        });
    }

    [Test]
    public async Task Removing_A_New_Child_Property_Leaves_Empty_Parent_Unchanged()
    {
        await Shield.Empty.ExecuteWithContextAsync(async parent =>
        {
            await Shield.Empty.ExecuteWithContextAsync(parent, static child =>
            {
                child.Properties.Set(ChildValue, 42);
                child.Properties.Remove(ChildValue);
                return ValueTask.CompletedTask;
            });

            await Assert.That(parent.Properties.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Empty_Nested_Bags_Share_Attempt_Suppression_In_Both_Directions()
    {
        await Shield.Empty.ExecuteWithContextAsync(async parent =>
        {
            await Shield.Empty.ExecuteWithContextAsync(parent, async child =>
            {
                parent.Properties.SuppressAdditionalAttempts = true;
                await Assert.That(child.Properties.SuppressAdditionalAttempts).IsTrue();
                parent.Properties.SuppressAdditionalAttempts = false;
                child.Properties.SuppressAdditionalAttempts = true;
                await Assert.That(parent.Properties.SuppressAdditionalAttempts).IsTrue();
            });

            await Assert.That(parent.Properties.SuppressAdditionalAttempts).IsTrue();
            await Assert.That(parent.Properties.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Empty_Nested_Bag_Propagates_Hedge_Completion_Properties()
    {
        var hedge = Shield.Hedge(1, TimeSpan.Zero);
        await Shield.Empty.ExecuteWithContextAsync(async parent =>
        {
            var calls = 0;
            var result = await hedge.ExecuteWithContextAsync(parent, async child =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, child.CancellationToken);
                }

                child.Properties.Set(ChildValue, 42);
                return 42;
            });

            await Assert.That(result).IsEqualTo(42);
            await Assert.That(parent.Properties.GetOrDefault(ChildValue)).IsEqualTo(42);
        });
    }

    [Test]
    public async Task Nested_Execution_Carries_Parent_State_And_Child_Mutations_Back()
    {
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var outer = Shield.Empty.WithTimeProvider(timeProvider).WithName("outer");
        var inner = Shield.Retry(0, Backoff.None).WithName("inner");

        var result = await outer.ExecuteWithContextAsync(
            "request-42",
            static (operationKey, properties) =>
            {
                properties.Set(RequestId, operationKey);
                properties.Set(KevlarKeys.OperationKey, operationKey);
            },
            async (_, parent) =>
            {
                var childResult = await inner.ExecuteWithContextAsync(
                    parent,
                    static context =>
                    {
                        if (context.Properties.GetOrDefault(RequestId, string.Empty) != "request-42"
                            || context.Properties.GetOrDefault(KevlarKeys.OperationKey, string.Empty) != "request-42")
                        {
                            throw new InvalidOperationException("Parent properties were not carried into the child.");
                        }

                        context.Properties.Set(ChildValue, 42);
                        return new ValueTask<int>(42);
                    });

                await Assert.That(parent.Properties.GetOrDefault(ChildValue)).IsEqualTo(42);
                return childResult;
            },
            cancellation.Token);

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Nested_Execution_Uses_Parent_Token_And_TimeProvider()
    {
        using var cancellation = new CancellationTokenSource();
        var timeProvider = new FakeTimeProvider();
        var observedToken = default(CancellationToken);
        TimeProvider? observedTimeProvider = null;

        await Shield.Empty.WithTimeProvider(timeProvider).ExecuteWithContextAsync(async parent =>
        {
            await Shield.Empty.ExecuteWithContextAsync(
                parent,
                context =>
                {
                    observedToken = context.CancellationToken;
                    observedTimeProvider = context.TimeProvider;
                    return ValueTask.CompletedTask;
                });
        }, cancellation.Token);

        await Assert.That(observedToken).IsEqualTo(cancellation.Token);
        await Assert.That(ReferenceEquals(observedTimeProvider, timeProvider)).IsTrue();
    }

    [Test]
    public async Task Synchronous_Nested_Execution_Recommends_Context_Async_Overload()
    {
        NotSupportedException? rejection = null;

        await Shield.Empty.ExecuteWithContextAsync(async parentContext =>
        {
            rejection = await Assert.That(() => Shield.For<int>()
                    .Hedge(1, TimeSpan.Zero)
                    .ExecuteWithContext(parentContext, static _ => 42))
                .Throws<NotSupportedException>();
        });

        await Assert.That(rejection!.Message)
            .Contains("Use ExecuteWithContextAsync instead of ExecuteWithContext.");
    }

    [Test]
    public async Task Synchronous_Nested_Hook_Rejection_Recommends_Context_Async_Overload()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ => await gate.Task;
        });
        NotSupportedException? rejection = null;

        await Shield.Empty.ExecuteWithContextAsync(async parentContext =>
        {
            rejection = await Assert.That(() => shield.ExecuteWithContext(
                    parentContext,
                    static _ => throw new InvalidOperationException()))
                .Throws<NotSupportedException>();
        });

        await Assert.That(rejection!.Message)
            .Contains("Use ExecuteWithContextAsync instead of ExecuteWithContext");
        gate.SetResult();
    }

    [Test]
    public async Task Parent_Cancellation_Cancels_Nested_Execution()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = Shield.Empty.ExecuteWithContextAsync(async parent =>
        {
            await Shield.Empty.ExecuteWithContextAsync(
                parent,
                async context =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                });
        }, cancellation.Token).AsTask();

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var exception = await Assert.That(async () => await execution).Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Child_Context_Returns_Independently_While_Parent_Remains_Valid()
    {
        KevlarContext? child = null;

        await Shield.Empty.ExecuteWithContextAsync(async parent =>
        {
            await Shield.Empty.ExecuteWithContextAsync(
                parent,
                context =>
                {
                    child = context;
                    return ValueTask.CompletedTask;
                });

            await Assert.That(ReferenceEquals(parent, child)).IsFalse();
            await Assert.That(parent.Properties.Count).IsEqualTo(0);
#if DEBUG
            await Assert.That(() => child!.Properties).Throws<InvalidOperationException>();
#endif
        });
    }

    [Test]
    public async Task Parent_Context_Has_An_Exact_Null_Guard_Across_Overload_Families()
    {
        var failures = new Action[]
        {
            () => Shield.Empty.ExecuteWithContextAsync(null!, static _ => new ValueTask<int>(42)),
            () => Shield.Empty.ExecuteWithContextAsync(null!, static _ => ValueTask.CompletedTask),
            () => Shield<int>.Empty.ExecuteWithContextAsync(null!, static _ => new ValueTask<int>(42)),
            () => Shield.Empty.ExecuteWithContextAsync(null!, static _ => Task.FromResult(42)),
            () => Shield.Empty.ExecuteWithContextAsync(null!, static _ => Task.CompletedTask),
            () => Shield<int>.Empty.ExecuteWithContextAsync(null!, static _ => Task.FromResult(42)),
            () => Shield.Empty.ExecuteWithContext(null!, static _ => 42),
            () => Shield.Empty.ExecuteWithContext(null!, static _ => { }),
            () => Shield<int>.Empty.ExecuteWithContext(null!, static _ => 42),
        };

        foreach (var failure in failures)
        {
            var exception = await Assert.That(failure).Throws<ArgumentNullException>();
            await Assert.That(exception!.ParamName).IsEqualTo("parentContext");
        }
    }

    [Test]
    public async Task Explicit_Default_Selects_Top_Level_Cancellation_Overloads()
    {
        var valueTaskResult = await Shield.Empty.ExecuteWithContextAsync(
            static context => new ValueTask<int>(context.CancellationToken.CanBeCanceled ? 0 : 1),
            default);
        await Shield.Empty.ExecuteWithContextAsync(static _ => ValueTask.CompletedTask, default);
        var taskResult = await Shield.Empty.ExecuteWithContextAsync(
            static context => Task.FromResult(context.CancellationToken.CanBeCanceled ? 0 : 1),
            default);
        await Shield.Empty.ExecuteWithContextAsync(static _ => Task.CompletedTask, default);
        var typedResult = await Shield<int>.Empty.ExecuteWithContextAsync(
            static context => new ValueTask<int>(context.CancellationToken.CanBeCanceled ? 0 : 1),
            default);
        var typedTaskResult = await Shield<int>.Empty.ExecuteWithContextAsync(
            static context => Task.FromResult(context.CancellationToken.CanBeCanceled ? 0 : 1),
            default);
        var syncResult = Shield.Empty.ExecuteWithContext(
            static context => context.CancellationToken.CanBeCanceled ? 0 : 1,
            default);
        Shield.Empty.ExecuteWithContext(static _ => { }, default);
        var typedSyncResult = Shield<int>.Empty.ExecuteWithContext(
            static context => context.CancellationToken.CanBeCanceled ? 0 : 1,
            default);

        await Assert.That(valueTaskResult).IsEqualTo(1);
        await Assert.That(taskResult).IsEqualTo(1);
        await Assert.That(typedResult).IsEqualTo(1);
        await Assert.That(typedTaskResult).IsEqualTo(1);
        await Assert.That(syncResult).IsEqualTo(1);
        await Assert.That(typedSyncResult).IsEqualTo(1);
    }

    [Test]
    public async Task Nested_Synchronous_Execution_Uses_And_Updates_Parent_Context()
    {
        var timeProvider = new FakeTimeProvider();

        await Shield.Empty.WithTimeProvider(timeProvider).ExecuteWithContextAsync(async parent =>
        {
            parent.Properties.Set(RequestId, "request-42");

            var result = Shield.Retry(0, Backoff.None).ExecuteWithContext(
                parent,
                (value: 40, timeProvider),
                static (state, child) =>
                {
                    child.Properties.Set(ChildValue, 2);
                    return state.value
                        + child.Properties.GetOrDefault(ChildValue)
                        + (child.Properties.GetOrDefault(RequestId, string.Empty) == "request-42" ? 0 : 100)
                        + (ReferenceEquals(child.TimeProvider, state.timeProvider) ? 0 : 1000);
                });

            await Assert.That(result).IsEqualTo(42);
            await Assert.That(parent.Properties.GetOrDefault(ChildValue)).IsEqualTo(2);
        });
    }
}

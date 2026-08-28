using Kevlar.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class PartitionedShieldTests
{
    [Test]
    public async Task Warm_Idle_Expiration_Hit_Does_Not_Wait_For_Mutation_Gate()
    {
        var cache = new PartitionCache<string, Shield>(
            static _ => new ValueTask<Shield>(Shield.Empty),
            new PartitionCacheOptions<string, Shield>(
                maxPartitions: 10,
                idleExpiration: TimeSpan.FromMinutes(1),
                TimeProvider.System,
                onCreated: null,
                onEvicted: null),
            comparer: null);
        var first = cache.Get("tenant");
        var mutationGate = (SemaphoreSlim)typeof(PartitionCache<string, Shield>)
            .GetField("_mutationGate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(cache)!;

        await mutationGate.WaitAsync();
        var lookup = cache.GetAsync("tenant");
        var completedSynchronously = lookup.IsCompletedSuccessfully;
        mutationGate.Release();
        var second = await lookup;

        await Assert.That(completedSynchronously).IsTrue();
        await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task Concurrent_First_Lookup_Creates_One_Partition()
    {
        var creations = 0;
        var provider = new PartitionedShield<string>(_ =>
        {
            Interlocked.Increment(ref creations);
            Thread.Sleep(10);
            return Shield.Retry(1, Backoff.None);
        });

        var lookups = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => provider.GetShield("tenant")))
            .ToArray();
        var shields = await Task.WhenAll(lookups);

        await Assert.That(creations).IsEqualTo(1);
        await Assert.That(provider.CreatedCount).IsEqualTo(1);
        await Assert.That(provider.Count).IsEqualTo(1);
        await Assert.That(shields.All(shield => ReferenceEquals(shield, shields[0]))).IsTrue();
    }

    [Test]
    public async Task Factory_Can_Wait_For_An_Unrelated_Provider_Lookup()
    {
        PartitionedShield<string>? provider = null;
        provider = new PartitionedShield<string>(key =>
        {
            if (key == "dependent")
            {
                var lookup = Task.Run(() => provider!.GetShield("warm"));
                if (!lookup.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The unrelated partition lookup was blocked by the factory.");
                }
            }

            return Shield.Empty;
        });
        _ = provider.GetShield("warm");

        var dependent = await Task.Run(() => provider.GetShield("dependent"))
            .WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(ReferenceEquals(dependent, Shield.Empty)).IsTrue();
        await Assert.That(provider.CreatedCount).IsEqualTo(2);
    }

    [Test]
    public async Task Capacity_Uses_Lru_And_Recreates_Fresh_State()
    {
        var provider = new PartitionedShield<string>(
            _ => Shield.CircuitBreaker(1, TimeSpan.FromHours(1)),
            new PartitionedShieldOptions<string> { MaxPartitions = 2 });
        var firstA = provider.GetShield("a");
        var firstB = provider.GetShield("b");
        _ = provider.GetShield("a");

        _ = provider.GetShield("c");

        await Assert.That(provider.TryGetShield("b", out _)).IsFalse();
        await Assert.That(provider.TryGetShield("a", out var retainedA)).IsTrue();
        await Assert.That(ReferenceEquals(retainedA, firstA)).IsTrue();
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(1);

        var oldResult = await firstB.ExecuteAsync(_ => new ValueTask<int>(42));
        var recreatedB = provider.GetShield("b");
        await Assert.That(oldResult).IsEqualTo(42);
        await Assert.That(ReferenceEquals(recreatedB, firstB)).IsFalse();
        await Assert.That(provider.CreatedCount).IsEqualTo(4);
    }

    [Test]
    public async Task Idle_Expiry_Is_Explicit_And_Opportunistic()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = new PartitionedShield<string>(
            _ => Shield.Retry(0, Backoff.None),
            new PartitionedShieldOptions<string>
            {
                MaxPartitions = 10,
                IdleExpiration = TimeSpan.FromMinutes(1),
                TimeProvider = timeProvider,
            });
        var first = provider.GetShield("tenant");

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var removed = provider.PruneExpired();
        var second = provider.GetShield("tenant");

        await Assert.That(removed).IsEqualTo(1);
        await Assert.That(provider.ExpirationEvictionCount).IsEqualTo(1);
        await Assert.That(provider.CreatedCount).IsEqualTo(2);
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }

    [Test]
    public async Task Idle_Eviction_Disposes_Owned_Strategies()
    {
        var timeProvider = new FakeTimeProvider();
        var strategy = new DisposablePartitionStrategy();
        var provider = new PartitionedShield<string>(
            _ => Shield.Use(strategy),
            new PartitionedShieldOptions<string>
            {
                IdleExpiration = TimeSpan.FromMinutes(1),
                TimeProvider = timeProvider,
            });
        _ = provider.GetShield("tenant");

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        _ = provider.PruneExpired();

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Publication_Disposes_Entries_That_Expire_During_The_Factory()
    {
        var timeProvider = new FakeTimeProvider();
        var factoryEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var expiredStrategy = new DisposablePartitionStrategy();
        var provider = PartitionedShield<string>.CreateAsync(
            async key =>
            {
                if (key == "publishing")
                {
                    factoryEntered.TrySetResult();
                    await releaseFactory.Task;
                    return Shield.Empty;
                }

                return Shield.Use(expiredStrategy);
            },
            new PartitionedShieldOptions<string>
            {
                MaxPartitions = 2,
                IdleExpiration = TimeSpan.FromMinutes(1),
                TimeProvider = timeProvider,
            });
        _ = await provider.GetShieldAsync("expired");
        var publishing = provider.GetShieldAsync("publishing").AsTask();
        await factoryEntered.Task;
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        releaseFactory.TrySetResult();
        _ = await publishing;

        await Assert.That(expiredStrategy.DisposeCount).IsEqualTo(1);
        await provider.DisposeAsync();
    }

    [Test]
    public async Task DisposeAsync_Disposes_All_Live_Partitions_Once()
    {
        var strategies = new List<DisposablePartitionStrategy>();
        var provider = new PartitionedShield<int>(_ =>
        {
            var strategy = new DisposablePartitionStrategy();
            strategies.Add(strategy);
            return Shield.Use(strategy);
        });
        _ = provider.GetShield(1);
        _ = provider.GetShield(2);

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        await Assert.That(strategies.Select(static strategy => strategy.DisposeCount))
            .IsEquivalentTo([1, 1]);
        await Assert.That(() => provider.GetShield(3)).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Shared_Strategy_Is_Disposed_After_Last_Partition_Is_Removed()
    {
        var strategy = new DisposablePartitionStrategy();
        var provider = new PartitionedShield<string>(_ => Shield.Use(strategy));
        _ = provider.GetShield("first");
        _ = provider.GetShield("second");

        _ = provider.TryRemove("first");
        await Assert.That(strategy.DisposeCount).IsEqualTo(0);

        _ = provider.TryRemove("second");
        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Typed_DisposeAsync_Prefers_Async_Strategy_Disposal()
    {
        var strategy = new AsyncDisposablePartitionStrategy();
        var provider = new PartitionedShield<string, int>(_ =>
            Shield.For<int>().Use(strategy));
        _ = provider.GetShield("tenant");

        await provider.DisposeAsync();

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task ServiceProvider_DisposeAsync_Disposes_Partitioned_Shields()
    {
        var strategy = new DisposablePartitionStrategy();
        var services = new ServiceCollection()
            .AddKevlar()
            .AddPartitionedShield<string>("tenant", (_, _) => Shield.Use(strategy))
            .BuildServiceProvider();
        var provider = services.GetRequiredKeyedService<PartitionedShield<string>>("tenant");
        _ = provider.GetShield("alpha");

        await services.DisposeAsync();

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Eviction_Defers_Disposal_Until_Active_Execution_Completes()
    {
        var firstStrategy = new DisposablePartitionStrategy();
        var provider = new PartitionedShield<string>(
            key => Shield.Use(key == "first" ? firstStrategy : new DisposablePartitionStrategy()),
            new PartitionedShieldOptions<string> { MaxPartitions = 1 });
        var first = provider.GetShield("first");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = first.ExecuteAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }).AsTask();
        await entered.Task;

        _ = provider.GetShield("second");
        await Assert.That(firstStrategy.DisposeCount).IsEqualTo(0);

        release.TrySetResult();
        await execution;
        await provider.DisposeAsync();

        await Assert.That(firstStrategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Eviction_Defers_All_Strategy_Disposal_For_The_Whole_Execution()
    {
        var pause = new PausingPartitionStrategy();
        var inner = new DisposablePartitionStrategy();
        var provider = new PartitionedShield<string>(
            key => key == "first"
                ? Shield.Use(pause).Use(inner)
                : Shield.Empty,
            new PartitionedShieldOptions<string> { MaxPartitions = 1 });
        var first = provider.GetShield("first");
        var execution = first.ExecuteAsync(static _ => ValueTask.CompletedTask).AsTask();
        await pause.Entered.Task;

        _ = provider.GetShield("second");

        await Assert.That(inner.DisposeCount).IsEqualTo(0);
        pause.Release.TrySetResult();
        await execution;
        await provider.DisposeAsync();
        await Assert.That(inner.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task DisposeAsync_Waits_For_Factories_And_Disposes_Rejected_Shields()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = new DisposablePartitionStrategy();
        var provider = PartitionedShield<string>.CreateAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            return Shield.Use(strategy);
        });
        var lookup = provider.GetShieldAsync("tenant").AsTask();
        await entered.Task;

        var firstDisposal = provider.DisposeAsync().AsTask();
        var secondDisposal = provider.DisposeAsync().AsTask();

        await Assert.That(firstDisposal.IsCompleted).IsFalse();
        await Assert.That(secondDisposal.IsCompleted).IsFalse();
        release.TrySetResult();
        _ = await Assert.That(async () => await lookup).Throws<ObjectDisposedException>();
        await firstDisposal;
        await secondDisposal;
        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Nested_Unretained_Partitions_Dispose_Their_Strategies()
    {
        PartitionedShield<string>? provider = null;
        DisposablePartitionStrategy? nestedStrategy = null;
        provider = new PartitionedShield<string>(
            key =>
            {
                var strategy = new DisposablePartitionStrategy();
                if (key == "nested")
                {
                    nestedStrategy = strategy;
                }

                return Shield.Use(strategy);
            },
            new PartitionedShieldOptions<string>
            {
                MaxPartitions = 1,
                OnEvicted = item =>
                {
                    if (item.Key == "first")
                    {
                        _ = provider!.GetShield("nested");
                    }

                    return ValueTask.CompletedTask;
                },
            });
        _ = provider.GetShield("first");

        _ = provider.GetShield("second");

        await Assert.That(nestedStrategy).IsNotNull();
        await Assert.That(nestedStrategy!.DisposeCount).IsEqualTo(1);
        await provider.DisposeAsync();
    }

    [Test]
    public async Task Nested_Eviction_Captured_Context_Uses_Active_Parent()
    {
        PartitionedShield<string>? provider = null;
        Task? nestedLookup = null;
        var releaseNestedLookup = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        provider = new PartitionedShield<string>(
            static _ => Shield.Use(new DisposablePartitionStrategy()),
            new PartitionedShieldOptions<string>
            {
                MaxPartitions = 2,
                OnEvicted = async item =>
                {
                    if (item.Key == "second")
                    {
                        nestedLookup = Task.Run(async () =>
                        {
                            await releaseNestedLookup.Task;
                            _ = await provider!.GetShieldAsync("third");
                        });
                        return;
                    }

                    if (item.Key == "first")
                    {
                        _ = await provider!.TryRemoveAsync("second");
                        releaseNestedLookup.TrySetResult();
                        await nestedLookup!;
                    }
                },
            });
        _ = provider.GetShield("first");
        _ = provider.GetShield("second");

        _ = await provider.GetShieldAsync("third").AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        await provider.DisposeAsync();
    }

    [Test]
    public async Task Callback_Creation_Is_Not_Shared_With_An_Ordinary_Waiter()
    {
        PartitionedShield<string>? provider = null;
        var nestedFactoryEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNestedFactory = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackReceivedShield = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Shield? callbackShield = null;
        var nestedFactoryCalls = 0;
        provider = PartitionedShield<string>.CreateAsync(
            async key =>
            {
                if (key == "nested" && Interlocked.Increment(ref nestedFactoryCalls) == 1)
                {
                    nestedFactoryEntered.TrySetResult();
                    await releaseNestedFactory.Task;
                }

                return Shield.Use(new DisposablePartitionStrategy());
            },
            new PartitionedShieldOptions<string>
            {
                MaxPartitions = 1,
                OnEvicted = async item =>
                {
                    if (item.Key != "first")
                    {
                        return;
                    }

                    callbackShield = await provider!.GetShieldAsync("nested");
                    callbackReceivedShield.TrySetResult();
                    await releaseCallback.Task;
                },
            });
        _ = await provider.GetShieldAsync("first");

        var capacityLookup = provider.GetShieldAsync("second").AsTask();
        await nestedFactoryEntered.Task;
        var ordinaryLookup = provider.GetShieldAsync("nested").AsTask();
        releaseNestedFactory.TrySetResult();
        await callbackReceivedShield.Task;

        await Assert.That(ordinaryLookup.IsCompleted).IsFalse();
        releaseCallback.TrySetResult();
        _ = await capacityLookup;
        var ordinaryShield = await ordinaryLookup;

        await Assert.That(ReferenceEquals(callbackShield, ordinaryShield)).IsFalse();
        await Assert.That(nestedFactoryCalls).IsEqualTo(2);
        await provider.DisposeAsync();
    }

    [Test]
    public async Task Shared_Strategy_Is_Disposed_After_All_Providers_Release_It()
    {
        var strategy = new DisposablePartitionStrategy();
        var first = new PartitionedShield<string>(_ => Shield.Use(strategy));
        var second = new PartitionedShield<string>(_ => Shield.Use(strategy));
        _ = first.GetShield("first");
        _ = second.GetShield("second");

        await first.DisposeAsync();
        await Assert.That(strategy.DisposeCount).IsEqualTo(0);

        await second.DisposeAsync();
        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Removing_A_Shield_Does_Not_Wait_For_Another_Shield_Sharing_Its_First_Strategy()
    {
        var shared = new DisposablePartitionStrategy();
        var firstUnique = new DisposablePartitionStrategy();
        var secondUnique = new DisposablePartitionStrategy();
        var provider = new PartitionedShield<string>(key =>
            Shield.Use(shared).Use(key == "first" ? firstUnique : secondUnique));
        _ = provider.GetShield("first");
        var second = provider.GetShield("second");
        var executionEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecution = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = second.ExecuteAsync(async _ =>
        {
            executionEntered.TrySetResult();
            await releaseExecution.Task;
        }).AsTask();
        await executionEntered.Task;

        _ = await provider.TryRemoveAsync("first");

        await Assert.That(firstUnique.DisposeCount).IsEqualTo(1);
        await Assert.That(shared.DisposeCount).IsEqualTo(0);
        releaseExecution.TrySetResult();
        await execution;
        await provider.DisposeAsync();
        await Assert.That(shared.DisposeCount).IsEqualTo(1);
        await Assert.That(secondUnique.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Externally_Owned_Strategies_Are_Not_Disposed()
    {
        var strategy = new DisposablePartitionStrategy();
        var standalone = Shield.Use(strategy);
        var provider = new PartitionedShield<string>(
            _ => standalone,
            new PartitionedShieldOptions<string> { OwnsStrategies = false });
        _ = provider.GetShield("tenant");

        await provider.DisposeAsync();

        await Assert.That(strategy.DisposeCount).IsEqualTo(0);
        await standalone.ExecuteAsync(static _ => ValueTask.CompletedTask);
    }

    [Test]
    public async Task DisposeAsync_Waits_For_InFlight_Removal_Callbacks()
    {
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = new DisposablePartitionStrategy();
        var provider = new PartitionedShield<string>(
            _ => Shield.Use(strategy),
            new PartitionedShieldOptions<string>
            {
                OnEvicted = async _ =>
                {
                    callbackEntered.TrySetResult();
                    await releaseCallback.Task;
                },
            });
        _ = provider.GetShield("tenant");
        var removal = provider.TryRemoveAsync("tenant").AsTask();
        await callbackEntered.Task;

        var disposal = provider.DisposeAsync().AsTask();

        await Assert.That(disposal.IsCompleted).IsFalse();
        releaseCallback.TrySetResult();
        await removal;
        await disposal;
        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task DisposeAsync_Waits_For_OnCreated_Before_Evicting_The_Entry()
    {
        var createdEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCreated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evicted = 0;
        var provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions<string>
            {
                OnCreated = async _ =>
                {
                    createdEntered.TrySetResult();
                    await releaseCreated.Task;
                },
                OnEvicted = _ =>
                {
                    Interlocked.Increment(ref evicted);
                    return default;
                },
            });
        var lookup = provider.GetShieldAsync("tenant").AsTask();
        await createdEntered.Task;

        var disposal = provider.DisposeAsync().AsTask();

        await Assert.That(evicted).IsEqualTo(0);
        await Assert.That(disposal.IsCompleted).IsFalse();
        releaseCreated.TrySetResult();
        _ = await lookup;
        await disposal;
        await Assert.That(evicted).IsEqualTo(1);
    }

    [Test]
    public async Task Capacity_Eviction_Keeps_The_Shield_Alive_Through_OnCreated()
    {
        var createdEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCreated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStrategy = new DisposablePartitionStrategy();
        var provider = new PartitionedShield<string>(
            key => Shield.Use(key == "first" ? firstStrategy : new DisposablePartitionStrategy()),
            new PartitionedShieldOptions<string>
            {
                MaxPartitions = 1,
                OnCreated = async created =>
                {
                    if (created.Key == "first")
                    {
                        createdEntered.TrySetResult();
                        await releaseCreated.Task;
                    }
                },
            });
        var firstLookup = provider.GetShieldAsync("first").AsTask();
        await createdEntered.Task;

        _ = await provider.GetShieldAsync("second");

        await Assert.That(firstStrategy.DisposeCount).IsEqualTo(0);
        releaseCreated.TrySetResult();
        _ = await firstLookup;
        await provider.DisposeAsync();
        await Assert.That(firstStrategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Disposal_Reentered_From_OnEvicted_Is_Rejected_Without_Deadlock()
    {
        PartitionedShield<string>? provider = null;
        Exception? disposalFailure = null;
        provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions<string>
            {
                OnEvicted = async _ =>
                {
                    try
                    {
                        await provider!.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        disposalFailure = exception;
                    }
                },
            });
        _ = provider.GetShield("tenant");

        _ = await provider.TryRemoveAsync("tenant").AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(disposalFailure).IsTypeOf<InvalidOperationException>();
        await provider.DisposeAsync();
    }

    [Test]
    public async Task Disposal_Reentered_From_OnCreated_Is_Rejected_Without_Deadlock()
    {
        PartitionedShield<string>? provider = null;
        Exception? disposalFailure = null;
        provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions<string>
            {
                OnCreated = async _ =>
                {
                    try
                    {
                        await provider!.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        disposalFailure = exception;
                    }
                },
            });

        _ = await provider.GetShieldAsync("tenant").AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(disposalFailure).IsTypeOf<InvalidOperationException>();
        await provider.DisposeAsync();
    }

    [Test]
    public async Task Disposal_Reentered_From_Factory_Is_Rejected_Without_Deadlock()
    {
        PartitionedShield<string>? provider = null;
        Exception? disposalFailure = null;
        provider = PartitionedShield<string>.CreateAsync(async _ =>
        {
            try
            {
                await provider!.DisposeAsync();
            }
            catch (Exception exception)
            {
                disposalFailure = exception;
            }

            return Shield.Empty;
        });

        _ = await provider.GetShieldAsync("tenant").AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(disposalFailure).IsTypeOf<InvalidOperationException>();
        await provider.DisposeAsync();
    }

    [Test]
    public async Task Disposal_Reentered_From_Execution_Is_Rejected_Without_Deadlock()
    {
        PartitionedShield<string>? provider = null;
        Exception? disposalFailure = null;
        provider = new PartitionedShield<string>(
            static _ => Shield.Use(new DisposablePartitionStrategy()));
        var shield = provider.GetShield("tenant");

        await shield.ExecuteAsync(async _ =>
        {
            try
            {
                await provider.DisposeAsync();
            }
            catch (Exception exception)
            {
                disposalFailure = exception;
            }
        });

        await Assert.That(disposalFailure).IsTypeOf<InvalidOperationException>();
        await provider.DisposeAsync();
    }

    [Test]
    public async Task DisposeAsync_Beside_InFlight_Execution_Waits_Instead_Of_Rejecting()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = new DisposablePartitionStrategy();
        var provider = new PartitionedShield<string>(_ => Shield.Use(strategy));
        var shield = provider.GetShield("tenant");
        var execution = shield.ExecuteAsync(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        }).AsTask();
        await entered.Task;

        var disposal = provider.DisposeAsync().AsTask();

        await Assert.That(disposal.IsCompleted).IsFalse();
        release.TrySetResult();
        await execution;
        await disposal;
        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Disposal_Reentered_From_Strategy_Disposal_Is_Rejected_Without_Deadlock()
    {
        var strategy = new ReentrantDisposalStrategy();
        var provider = new PartitionedShield<string>(_ => Shield.Use(strategy));
        strategy.Provider = provider;
        _ = provider.GetShield("tenant");

        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(strategy.DisposalFailure).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task OnCreated_Captured_Context_Allows_Disposal_After_Callback_Returns()
    {
        PartitionedShield<string>? provider = null;
        Task<Exception?>? delayedDisposal = null;
        var releaseDisposal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        provider = new PartitionedShield<string>(
            static _ => Shield.Empty,
            new PartitionedShieldOptions<string>
            {
                OnCreated = _ =>
                {
                    delayedDisposal = DisposeAfterReleaseAsync(provider!, releaseDisposal.Task);
                    return default;
                },
            });

        _ = await provider.GetShieldAsync("tenant");
        releaseDisposal.TrySetResult();

        await Assert.That(await delayedDisposal!).IsNull();

        static async Task<Exception?> DisposeAfterReleaseAsync(
            PartitionedShield<string> provider,
            Task release)
        {
            await release;
            try
            {
                await provider.DisposeAsync();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    [Test]
    public async Task Synchronous_Removal_And_Disposal_Prefer_IDisposable()
    {
        var removedStrategy = new DualDisposablePartitionStrategy();
        var removedProvider = new PartitionedShield<string>(_ => Shield.Use(removedStrategy));
        _ = removedProvider.GetShield("tenant");

        _ = removedProvider.TryRemove("tenant");

        var clearedStrategy = new DualDisposablePartitionStrategy();
        var clearedProvider = new PartitionedShield<string>(_ => Shield.Use(clearedStrategy));
        _ = clearedProvider.GetShield("tenant");

        clearedProvider.Clear();

        var disposedStrategy = new DualDisposablePartitionStrategy();
        var disposedProvider = new PartitionedShield<string>(_ => Shield.Use(disposedStrategy));
        _ = disposedProvider.GetShield("tenant");

        disposedProvider.Dispose();

        await Assert.That(removedStrategy.SyncDisposeCount).IsEqualTo(1);
        await Assert.That(clearedStrategy.SyncDisposeCount).IsEqualTo(1);
        await Assert.That(disposedStrategy.SyncDisposeCount).IsEqualTo(1);
        await Assert.That(removedStrategy.AsyncDisposeCount).IsEqualTo(0);
        await Assert.That(clearedStrategy.AsyncDisposeCount).IsEqualTo(0);
        await Assert.That(disposedStrategy.AsyncDisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Concurrent_DisposeAsync_Callers_Observe_The_Same_Failure()
    {
        var strategy = new CoordinatedAsyncDisposableStrategy();
        var provider = new PartitionedShield<string>(_ => Shield.Use(strategy));
        _ = provider.GetShield("tenant");

        var first = provider.DisposeAsync().AsTask();
        await strategy.DisposeEntered.Task;
        var second = provider.DisposeAsync().AsTask();

        await Assert.That(second.IsCompleted).IsFalse();
        strategy.ReleaseDispose.TrySetResult();
        _ = await Assert.That(async () => await first).Throws<InvalidOperationException>();
        _ = await Assert.That(async () => await second).Throws<InvalidOperationException>();
        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Circuit_State_Is_Shared_Within_A_Key_And_Isolated_Between_Keys()
    {
        var provider = new PartitionedShield<string>(_ =>
            Shield.When<InvalidOperationException>()
                .CircuitBreaker(1, TimeSpan.FromHours(1)));
        var calls = 0;

        _ = await Assert.That(async () =>
                await provider.GetShield("a").ExecuteAsync<int>(_ =>
                {
                    calls++;
                    throw new InvalidOperationException();
                }))
            .Throws<InvalidOperationException>();
        _ = await Assert.That(async () =>
                await provider.GetShield("a").ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
        var other = await provider.GetShield("b").ExecuteAsync(_ =>
        {
            calls++;
            return new ValueTask<int>(2);
        });

        await Assert.That(other).IsEqualTo(2);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_Partitions_Preserve_Result_Aware_State()
    {
        var provider = new PartitionedShield<string, int>(_ =>
            Shield.For<int>().WhenResultEquals(-1).FallbackTo(0));

        var result = await provider.GetShield("typed")
            .ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(result).IsEqualTo(0);
        await Assert.That(provider.TryGetShield("typed", out var retained)).IsTrue();
        await Assert.That(ReferenceEquals(retained, provider.GetShield("typed"))).IsTrue();
    }

    [Test]
    public async Task Fallback_Partitions_Preserve_The_Shield_And_State()
    {
        var fallbackCalls = 0;
        var provider = new PartitionedShield<string>(_ =>
            Shield.When<InvalidOperationException>().Fallback((_, _) =>
            {
                fallbackCalls++;
                return ValueTask.CompletedTask;
            }));

        await provider.GetShield("void").ExecuteAsync(static _ =>
            ValueTask.FromException(new InvalidOperationException()));

        await Assert.That(fallbackCalls).IsEqualTo(1);
        await Assert.That(provider.TryGetShield("void", out var retained)).IsTrue();
        await Assert.That(ReferenceEquals(retained, provider.GetShield("void"))).IsTrue();
        await Assert.That(provider.CreatedCount).IsEqualTo(1);
    }

    [Test]
    public async Task Evicting_An_Active_Partition_Does_Not_Cancel_Its_Execution()
    {
        var provider = new PartitionedShield<string>(
            _ => Shield.Retry(0, Backoff.None),
            new PartitionedShieldOptions<string> { MaxPartitions = 1 });
        var first = provider.GetShield("first");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var execution = first.ExecuteOutcomeAsync<int>(async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();
        await entered.Task;

        _ = provider.GetShield("second");
        cancellation.Cancel();
        var outcome = await execution;
        var recreated = provider.GetShield("first");

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(ReferenceEquals(first, recreated)).IsFalse();
    }

    [Test]
    public async Task Factory_Failure_Does_Not_Evict_A_Retained_Partition()
    {
        var provider = new PartitionedShield<string>(
            key => key == "bad" ? throw new InvalidOperationException("factory") : Shield.Empty,
            new PartitionedShieldOptions<string> { MaxPartitions = 1 });
        var retained = provider.GetShield("good");

        _ = await Assert.That(() => provider.GetShield("bad")).Throws<InvalidOperationException>();

        await Assert.That(provider.TryGetShield("good", out var actual)).IsTrue();
        await Assert.That(ReferenceEquals(actual, retained)).IsTrue();
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(0);
    }

    [Test]
    public async Task Remove_And_Clear_Do_Not_Count_As_Automatic_Evictions()
    {
        var provider = new PartitionedShield<int>(_ => Shield.Empty);
        _ = provider.GetShield(1);
        _ = provider.GetShield(2);

        await Assert.That(provider.TryRemove(1)).IsTrue();
        await Assert.That(provider.TryRemove(1)).IsFalse();
        provider.Clear();

        await Assert.That(provider.Count).IsEqualTo(0);
        await Assert.That(provider.CapacityEvictionCount).IsEqualTo(0);
        await Assert.That(provider.ExpirationEvictionCount).IsEqualTo(0);
    }

    [Test]
    public async Task Invalid_Options_And_Keys_Are_Rejected()
    {
        _ = await Assert.That(() => new PartitionedShield<string>(
                _ => Shield.Empty,
                new PartitionedShieldOptions<string> { MaxPartitions = 0 }))
            .Throws<ArgumentOutOfRangeException>();
        _ = await Assert.That(() => new PartitionedShield<string>(
                _ => Shield.Empty,
                new PartitionedShieldOptions<string> { IdleExpiration = TimeSpan.Zero }))
            .Throws<ArgumentOutOfRangeException>();
        _ = await Assert.That(() => new PartitionedShield<string>(null!))
            .Throws<ArgumentNullException>();

        var provider = new PartitionedShield<string>(_ => Shield.Empty);
        _ = await Assert.That(() => provider.GetShield(null!)).Throws<ArgumentNullException>();
    }

    private sealed class DisposablePartitionStrategy : Strategy, IDisposable
    {
        public int DisposeCount { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            next.InvokeAsync(context);

        public void Dispose() => DisposeCount++;
    }

    private sealed class AsyncDisposablePartitionStrategy : Strategy, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            next.InvokeAsync(context);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return default;
        }
    }

    private sealed class PausingPartitionStrategy : Strategy
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected internal override bool InvokesContinuationAtMostOnce => true;

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Entered.TrySetResult();
            await Release.Task;
            return await next.InvokeAsync(context);
        }
    }

    private sealed class CoordinatedAsyncDisposableStrategy : Strategy, IAsyncDisposable
    {
        public TaskCompletionSource DisposeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            next.InvokeAsync(context);

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await ReleaseDispose.Task;
            DisposeCount++;
            throw new InvalidOperationException("dispose");
        }
    }

    private sealed class DualDisposablePartitionStrategy : Strategy, IDisposable, IAsyncDisposable
    {
        public int SyncDisposeCount { get; private set; }

        public int AsyncDisposeCount { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            next.InvokeAsync(context);

        public void Dispose() => SyncDisposeCount++;

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return ValueTask.FromException(new InvalidOperationException("async disposal"));
        }
    }

    private sealed class ReentrantDisposalStrategy : Strategy, IAsyncDisposable
    {
        public PartitionedShield<string>? Provider { get; set; }

        public Exception? DisposalFailure { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            next.InvokeAsync(context);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Provider!.DisposeAsync();
            }
            catch (Exception exception)
            {
                DisposalFailure = exception;
            }
        }
    }
}

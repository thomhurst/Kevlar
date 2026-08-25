using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kevlar.Tests;

public class DynamicRegistryTests
{
    [Test]
    public async Task GetOrAdd_Builds_Once_Per_Name_Under_Concurrency()
    {
        const int WorkerCount = 64;
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var factoryCalls = 0;
        using var ready = new CountdownEvent(WorkerCount);
        using var start = new ManualResetEventSlim();

        var resolutions = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    start.Wait();
                    return registry.GetOrAdd("tenant", _ =>
                    {
                        Interlocked.Increment(ref factoryCalls);
                        Thread.SpinWait(500_000);
                        return Shield.Empty;
                    });
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        ready.Wait();
        start.Set();
        var shields = await Task.WhenAll(resolutions);

        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(shields.All(shield => ReferenceEquals(shield, shields[0]))).IsTrue();
    }

    [Test]
    public async Task GetOrAdd_Returns_Existing_Registration_And_Separates_Typed_Shape()
    {
        var existing = Shield.Retry(0, Backoff.None);
        var typed = Shield.For<int>().FallbackTo(42);
        using var services = new ServiceCollection()
            .AddShield("shared", existing)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var ignoredFactoryCalls = 0;

        var untyped = registry.GetOrAdd("shared", _ =>
        {
            ignoredFactoryCalls++;
            return Shield.Empty;
        });
        var resolvedTyped = registry.GetOrAdd("shared", _ => typed);

        await Assert.That(ReferenceEquals(untyped, existing)).IsTrue();
        await Assert.That(ReferenceEquals(resolvedTyped, typed)).IsTrue();
        await Assert.That(ignoredFactoryCalls).IsEqualTo(0);
    }

    [Test]
    public async Task GetOrAdd_Factory_Exception_Is_Not_Cached()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var calls = 0;

        Shield Build(IServiceProvider _)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("transient factory failure");
            }

            return Shield.Retry(0, Backoff.None);
        }

        await Assert.That(() => registry.GetOrAdd("retryable", Build))
            .Throws<InvalidOperationException>();
        var recovered = registry.GetOrAdd("retryable", Build);

        await Assert.That(recovered).IsNotNull();
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Shield_Factory_Can_Resolve_Another_Registered_Shield()
    {
        var dependency = Shield.Retry(0, Backoff.None);
        using var services = new ServiceCollection()
            .AddShield("dependency", dependency)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();

        var composed = registry.GetOrAdd(
            "composed",
            serviceProvider => serviceProvider
                .GetRequiredService<IKevlarRegistry>()
                .GetShield("dependency"));

        await Assert.That(ReferenceEquals(composed, dependency)).IsTrue();
    }

    [Test]
    public async Task Concurrent_GetOrAdd_And_Remove_Do_Not_Expose_Partial_Entries()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var failures = new ConcurrentQueue<Exception>();

        Parallel.For(0, 10_000, operation =>
        {
            try
            {
                if ((operation & 1) == 0)
                {
                    _ = registry.GetOrAdd("contended", _ => Shield.Empty);
                }
                else
                {
                    registry.Remove("contended");
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task TryAdd_Remove_And_GetOrAdd_Have_Pinned_Lifetime_Semantics()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var first = Shield.Retry(0, Backoff.None);
        var replacement = Shield.Timeout(TimeSpan.FromSeconds(1));

        await Assert.That(registry.TryAdd("plugin", _ => first)).IsTrue();
        await Assert.That(registry.TryAdd("plugin", _ => replacement)).IsFalse();
        var held = registry.GetShield("plugin");
        await Assert.That(registry.Remove("plugin")).IsTrue();
        await Assert.That(registry.TryGetShield("plugin", out _)).IsFalse();
        await Assert.That(held.Execute(static _ => 42)).IsEqualTo(42);

        var rebuilt = registry.GetOrAdd("plugin", _ => replacement);
        await Assert.That(ReferenceEquals(rebuilt, replacement)).IsTrue();
        await Assert.That(ReferenceEquals(rebuilt, held)).IsFalse();
    }

    [Test]
    public async Task Dynamic_Names_Are_Registry_Only_Not_Keyed_Services()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        _ = registry.GetOrAdd("dynamic", _ => Shield.Empty);

        await Assert.That(services.GetKeyedService<Shield>("dynamic")).IsNull();
    }

    [Test]
    public async Task Registry_Is_Thread_Safe_Under_Add_Remove_Get_Stress()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var failures = new ConcurrentQueue<Exception>();

        Parallel.For(0, 1_000, operation =>
        {
            var name = $"shield-{operation % 16}";
            try
            {
                switch (operation % 4)
                {
                    case 0:
                        registry.TryAdd(name, _ => Shield.Empty);
                        break;
                    case 1:
                        _ = registry.GetOrAdd(name, _ => Shield.Retry(0, Backoff.None));
                        break;
                    case 2:
                        registry.Remove(name);
                        break;
                    default:
                        registry.TryGetShield(name, out _);
                        break;
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        await Assert.That(failures).IsEmpty();
        await Assert.That(registry.GetOrAdd("final", _ => Shield.Empty)).IsNotNull();
    }

    [Test]
    public async Task Options_Monitor_Reload_Rebuilds_Only_The_Changed_Name()
    {
        var monitor = new MutableOptionsMonitor<ReloadOptions>();
        monitor.Set("alpha", new ReloadOptions { Retries = 1 }, notify: false);
        monitor.Set("beta", new ReloadOptions { Retries = 2 }, notify: false);
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(monitor)
            .AddReloadingShield<ReloadOptions>(
                "alpha",
                static (options, _) => Shield.Retry(options.Retries, Backoff.None))
            .AddReloadingShield<ReloadOptions>(
                "beta",
                static (options, _) => Shield.Retry(options.Retries, Backoff.None))
            .BuildServiceProvider();
        var alpha = services.GetRequiredKeyedService<IShieldProvider>("alpha");
        var beta = services.GetRequiredKeyedService<IShieldProvider>("beta");
        var originalAlpha = alpha.Current;
        var originalBeta = beta.Current;

        monitor.Set("alpha", new ReloadOptions { Retries = 4 });

        await Assert.That(alpha.Current.ToString()).Contains("Retry(4");
        await Assert.That(ReferenceEquals(alpha.Current, originalAlpha)).IsFalse();
        await Assert.That(ReferenceEquals(beta.Current, originalBeta)).IsTrue();
    }

    [Test]
    public async Task Invalid_Options_Keep_Last_Known_Good_And_Report_Failure()
    {
        var monitor = new MutableOptionsMonitor<ReloadOptions>();
        monitor.Set("guarded", new ReloadOptions { Retries = 1 }, notify: false);
        Exception? reported = null;
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(monitor)
            .AddReloadingShield<ReloadOptions>(
                "guarded",
                static (options, _) => options.Retries < 0
                    ? throw new InvalidOperationException("invalid retries")
                    : Shield.Retry(options.Retries, Backoff.None),
                exception => reported = exception)
            .BuildServiceProvider();
        var provider = services.GetRequiredKeyedService<IShieldProvider>("guarded");
        var original = provider.Current;

        monitor.Set("guarded", new ReloadOptions { Retries = -1 });

        await Assert.That(ReferenceEquals(provider.Current, original)).IsTrue();
        await Assert.That(reported).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task Options_Monitor_With_No_Change_Subscription_Remains_Usable_And_Disposable()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(new NullSubscriptionOptionsMonitor())
            .AddReloadingShield<ReloadOptions>(
                "nullable-subscription",
                static (_, _) => Shield.Empty)
            .BuildServiceProvider();

        var provider = services.GetRequiredKeyedService<IShieldProvider>("nullable-subscription");

        await Assert.That(provider.Current).IsNotNull();
        services.Dispose();
    }

    [Test]
    public async Task Immediate_Change_Callback_Waits_For_Initial_Snapshot()
    {
        Exception? reported = null;
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(new ImmediateCallbackOptionsMonitor())
            .AddReloadingShield<ReloadOptions>(
                "immediate",
                static (_, _) => Shield.Retry(1, Backoff.None),
                exception => reported = exception)
            .BuildServiceProvider();

        var provider = services.GetRequiredKeyedService<IShieldProvider>("immediate");

        await Assert.That(provider.Current).IsNotNull();
        await Assert.That(reported).IsNull();
    }

    [Test]
    public async Task Superseded_Reload_Snapshot_Is_Reclaimed_After_Holders_Release_It()
    {
        var monitor = new MutableOptionsMonitor<ReloadOptions>();
        monitor.Set("reclaim", new ReloadOptions(), notify: false);
        var first = new DisposableStrategy();
        var second = new DisposableStrategy();
        var factoryCalls = 0;
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(monitor)
            .AddReloadingShield<ReloadOptions>(
                "reclaim",
                (_, _) => Shield.Use(Interlocked.Increment(ref factoryCalls) == 1 ? first : second))
            .BuildServiceProvider();
        var provider = services.GetRequiredKeyedService<IShieldProvider>("reclaim");

        var retired = ReplaceCurrentSnapshot(provider, monitor, "reclaim");
        Collect(retired);
        monitor.Set("reclaim", new ReloadOptions());

        await Assert.That(retired.IsAlive).IsFalse();
        await Assert.That(first.DisposeCount).IsEqualTo(1);
        await Assert.That(second.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Superseded_Reload_Snapshot_Is_Not_Disposed_During_Execution()
    {
        var monitor = new MutableOptionsMonitor<ReloadOptions>();
        monitor.Set("active", new ReloadOptions(), notify: false);
        var first = new DisposableStrategy();
        var second = new DisposableStrategy();
        var factoryCalls = 0;
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(monitor)
            .AddReloadingShield<ReloadOptions>(
                "active",
                (_, _) => Shield.Use(Interlocked.Increment(ref factoryCalls) == 1 ? first : second))
            .BuildServiceProvider();
        var provider = services.GetRequiredKeyedService<IShieldProvider>("active");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = provider.Current;
        var execution = current.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
        }).AsTask();
        await started.Task;

        monitor.Set("active", new ReloadOptions());
        var retired = new WeakReference(current);
        current = null!;
        Collect(retired);
        monitor.Set("active", new ReloadOptions());

        await Assert.That(first.DisposeCount).IsEqualTo(0);

        release.SetResult();
        await execution;
        Collect(retired);
        monitor.Set("active", new ReloadOptions());

        await Assert.That(first.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Reload_Retirement_Handoff_Remains_Inside_Publication_Guard()
    {
        using var publicationReturned = new ManualResetEventSlim();
        using var releasePublication = new ManualResetEventSlim();
        using var handedOff = new ManualResetEventSlim();
        var monitor = new MutableOptionsMonitor<ReloadOptions>();
        monitor.Set("handoff", new ReloadOptions(), notify: false);
        using var provider = new OptionsReloadingShieldProvider<ReloadOptions>(
            monitor,
            "handoff",
            static _ => Shield.Use(new DisposableStrategy()),
            onReloadFailure: null);
        var blockPublication = 0;
        ((IReloadingProvider)provider).SetLifecycleHandlers(
            retirements =>
            {
                if (retirements.Count > 0)
                {
                    handedOff.Set();
                }
            },
            publish =>
            {
                publish();
                if (Volatile.Read(ref blockPublication) != 0)
                {
                    publicationReturned.Set();
                    if (!releasePublication.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("The publication guard was not released.");
                    }
                }
            });

        var retired = ReplaceCurrentSnapshot(provider, monitor, "handoff");
        Collect(retired);
        await Assert.That(retired.IsAlive).IsFalse();
        Volatile.Write(ref blockPublication, 1);
        var reload = Task.Factory.StartNew(
            () => monitor.Set("handoff", new ReloadOptions()),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            await Assert.That(publicationReturned.Wait(TimeSpan.FromSeconds(5))).IsTrue();
            await Assert.That(handedOff.IsSet).IsTrue();
        }
        finally
        {
            releasePublication.Set();
            await reload;
        }
    }

    [Test]
    public async Task Typed_Options_Monitor_Reload_Publishes_Typed_Shield()
    {
        var monitor = new MutableOptionsMonitor<ReloadOptions>();
        monitor.Set("typed", new ReloadOptions { Fallback = 7 }, notify: false);
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(monitor)
            .AddReloadingShield<ReloadOptions, int>(
                "typed",
                static (options, _) => Shield.For<int>().FallbackTo(options.Fallback))
            .BuildServiceProvider();
        var provider = services.GetRequiredKeyedService<IShieldProvider<int>>("typed");

        await Assert.That(provider.Current.Execute(static _ => throw new InvalidOperationException()))
            .IsEqualTo(7);
        monitor.Set("typed", new ReloadOptions { Fallback = 9 });
        await Assert.That(provider.Current.Execute(static _ => throw new InvalidOperationException()))
            .IsEqualTo(9);
    }

    [Test]
    public async Task Registry_Dispose_Is_Idempotent_Disposes_Strategies_And_Rejects_Use()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new DisposableStrategy();
        _ = registry.GetOrAdd("owned", _ => Shield.Use(strategy));

        registry.Dispose();
        registry.Dispose();

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
        await Assert.That(() => registry.GetShield("owned")).Throws<ObjectDisposedException>();
        await Assert.That(() => registry.TryAdd("late", _ => Shield.Empty))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Removed_Resolved_Shield_Is_Disposed_With_Registry()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new DisposableStrategy();
        _ = registry.GetOrAdd("removed", _ => Shield.Use(strategy));

        await Assert.That(registry.Remove("removed")).IsTrue();
        services.Dispose();

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Remove_Captures_Reload_Retirements_When_Subscription_Disposal_Fails()
    {
        var strategy = new DisposableStrategy();
        var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(new ThrowingSubscriptionOptionsMonitor())
            .AddReloadingShield<ReloadOptions>(
                "throwing-subscription",
                (_, _) => Shield.Use(strategy))
            .BuildServiceProvider();

        try
        {
            _ = services.GetRequiredKeyedService<IShieldProvider>("throwing-subscription");
            var registry = services.GetRequiredService<IKevlarRegistry>();

            await Assert.That(registry.Remove("throwing-subscription")).IsTrue();
            await Assert.That(() => registry.Dispose()).Throws<InvalidOperationException>();
            await Assert.That(strategy.DisposeCount).IsEqualTo(1);
        }
        finally
        {
            services.Dispose();
        }
    }

    [Test]
    public async Task Removed_Resolved_Shield_Is_Reclaimed_After_Holders_Release_It()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new DisposableStrategy();
        var retired = ResolveAndRemove(registry, strategy);

        Collect(retired);
        _ = registry.GetOrAdd("scavenge", _ => Shield.Empty);

        await Assert.That(retired.IsAlive).IsFalse();
        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Async_Retirement_Can_Reenter_Registry_After_Yielding()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new ReentrantAsyncDisposableStrategy(() =>
            _ = registry.GetOrAdd("async-disposal-reentry", _ => Shield.Empty));
        var retired = ResolveAndRemove(registry, "async-retired", strategy);
        Collect(retired);

        var scavenging = Task.Run(() =>
            registry.GetOrAdd("async-scavenge", _ => Shield.Empty));

        _ = await scavenging.WaitAsync(TimeSpan.FromSeconds(5));
        await strategy.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(retired.IsAlive).IsFalse();
        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Pending_Async_Retirement_Cannot_Republish_Its_Strategy()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new BlockingAsyncDisposableStrategy();
        var retired = ResolveAndRemove(registry, "pending-async", strategy);
        Collect(retired);
        var scavenging = Task.Run(() =>
            registry.GetOrAdd("pending-async-scavenge", _ => Shield.Empty));
        await strategy.DisposalStarted.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            _ = await Assert.That(() =>
                    registry.GetOrAdd("pending-async-republished", _ => Shield.Use(strategy)))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            strategy.ReleaseDisposal();
            _ = await scavenging.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Execution_Started_Before_Resolution_Delays_Retirement()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new DisposableStrategy();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (execution, retired) = StartBeforeRegistryResolution(
            registry,
            strategy,
            started,
            release);
        await started.Task;

        Collect(retired);
        _ = registry.GetOrAdd("pre-resolution-scavenge", _ => Shield.Empty);

        await Assert.That(strategy.DisposeCount).IsEqualTo(0);

        release.SetResult();
        await execution;
    }

    [Test]
    public async Task Derived_Shields_Keep_Retired_Strategies_Alive()
    {
        Func<Shield, Shield>[] derivations =
        [
            static shield => shield.WithName("derived"),
            static shield => shield.Wrap(Shield.Empty),
            static shield => Shield.Compose(shield, Shield.Empty),
        ];

        for (var index = 0; index < derivations.Length; index++)
        {
            using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
            var registry = services.GetRequiredService<IKevlarRegistry>();
            var strategy = new DisposableStrategy();
            var (retired, released) = ExerciseDerivedShield(
                registry,
                strategy,
                derivations[index],
                index);
            Collect(released);
            _ = registry.GetOrAdd($"released-scavenge-{index}", _ => Shield.Empty);

            await Assert.That(retired.IsAlive).IsFalse();
            await Assert.That(released.IsAlive).IsFalse();
            await Assert.That(strategy.DisposeCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Remove_While_Factory_Runs_Retires_The_Result()
    {
        using var factoryStarted = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new DisposableStrategy();
        var resolving = Task.Factory.StartNew(
            () => registry.GetOrAdd("overlap", _ =>
            {
                factoryStarted.Set();
                if (!releaseFactory.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The removal did not release the shield factory.");
                }

                return Shield.Use(strategy);
            }),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        await Assert.That(factoryStarted.Wait(TimeSpan.FromSeconds(5))).IsTrue();
        await Assert.That(registry.Remove("overlap")).IsTrue();
        releaseFactory.Set();
        _ = await resolving;

        registry.Dispose();

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Reclamation_Waits_For_Concurrent_Strategy_Publication()
    {
        using var factoryStarted = new ManualResetEventSlim();
        using var triggerFactoryStarted = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new DisposableStrategy();
        var retired = ResolveAndRemove(registry, strategy);
        Collect(retired);
        var replacement = Task.Factory.StartNew(
            () => registry.GetOrAdd("replacement", _ =>
            {
                factoryStarted.Set();
                if (!releaseFactory.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The reclamation test did not release the shield factory.");
                }

                return Shield.Use(strategy);
            }),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await Assert.That(factoryStarted.Wait(TimeSpan.FromSeconds(5))).IsTrue();
        var scavenging = Task.Run(() => registry.GetOrAdd("scavenge", _ =>
        {
            triggerFactoryStarted.Set();
            return Shield.Empty;
        }));
        await Assert.That(triggerFactoryStarted.Wait(TimeSpan.FromSeconds(5))).IsTrue();

        _ = await scavenging;
        await Assert.That(strategy.DisposeCount).IsEqualTo(0);
        releaseFactory.Set();
        _ = await replacement;

        await Assert.That(strategy.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Reclamation_Waits_For_Reload_Publication()
    {
        using var factoryStarted = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        var monitor = new MutableOptionsMonitor<ReloadOptions>();
        monitor.Set("reload-race", new ReloadOptions(), notify: false);
        var strategy = new DisposableStrategy();
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(monitor)
            .AddReloadingShield<ReloadOptions>("reload-race", (options, _) =>
            {
                if (options.Retries == 0)
                {
                    return Shield.Empty;
                }

                factoryStarted.Set();
                if (!releaseFactory.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The reclamation test did not release the reload factory.");
                }

                return Shield.Use(strategy);
            })
            .BuildServiceProvider();
        _ = services.GetRequiredKeyedService<IShieldProvider>("reload-race");
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var retired = ResolveAndRemove(registry, strategy);
        Collect(retired);
        var reload = Task.Factory.StartNew(
            () => monitor.Set("reload-race", new ReloadOptions { Retries = 1 }),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await Assert.That(factoryStarted.Wait(TimeSpan.FromSeconds(5))).IsTrue();

        _ = registry.GetOrAdd("reload-scavenge", _ => Shield.Empty);
        await Assert.That(strategy.DisposeCount).IsEqualTo(0);
        releaseFactory.Set();
        await reload;

        await Assert.That(strategy.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Reentrant_Publication_During_Reclamation_Retains_Shared_Strategy()
    {
        using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var shared = new DisposableStrategy();
        var callback = new CallbackDisposableStrategy(() =>
            _ = registry.GetOrAdd("reentrant-replacement", _ => Shield.Use(shared)));
        var retired = ResolveAndRemove(registry, shared, callback);

        Collect(retired);
        _ = registry.GetOrAdd("reentrant-scavenge", _ => Shield.Empty);

        await Assert.That(callback.DisposeCount).IsEqualTo(1);
        await Assert.That(shared.DisposeCount).IsEqualTo(0);

        registry.Dispose();

        await Assert.That(shared.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Shared_Strategy_Is_Disposed_Once_Across_Retirement_Batches()
    {
        var strategy = new DisposableStrategy();
        var firstShield = Shield.Use(strategy);
        var secondShield = Shield.Use(strategy);
        var first = new ShieldRetirement(firstShield, firstShield);
        var second = new ShieldRetirement(secondShield, secondShield);
        var disposalTracker = new StrategyDisposalTracker();

        Parallel.Invoke(
            () => first.Reclaim(
                static exception => throw exception,
                ShieldRetirement.CreateStrategySet(),
                disposalTracker),
            () => second.Reclaim(
                static exception => throw exception,
                ShieldRetirement.CreateStrategySet(),
                disposalTracker));

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Disposal_Claims_Do_Not_Retain_Strategies()
    {
        var disposalTracker = new StrategyDisposalTracker();
        var strategy = ClaimDisposableStrategy(disposalTracker);

        Collect(strategy);

        await Assert.That(strategy.IsAlive).IsFalse();
    }

    [Test]
    public async Task Direct_Reloading_Provider_Resolution_Registers_Snapshots_For_Disposal()
    {
        var monitor = new MutableOptionsMonitor<ReloadOptions>();
        monitor.Set("direct", new ReloadOptions(), notify: false);
        var strategy = new DisposableStrategy();
        using var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<ReloadOptions>>(monitor)
            .AddReloadingShield<ReloadOptions>(
                "direct",
                (_, _) => Shield.Use(strategy))
            .BuildServiceProvider();

        _ = services.GetRequiredKeyedService<IShieldProvider>("direct");
        services.Dispose();

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Registry_DisposeAsync_Prefers_Async_Strategy_Disposal()
    {
        await using var services = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var strategy = new AsyncDisposableStrategy();
        _ = registry.GetOrAdd("async-owned", _ => Shield.Use(strategy));

        await registry.DisposeAsync();

        await Assert.That(strategy.DisposeCount).IsEqualTo(1);
    }

    private sealed class ReloadOptions
    {
        public int Retries { get; init; }

        public int Fallback { get; init; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceCurrentSnapshot(
        IShieldProvider provider,
        MutableOptionsMonitor<ReloadOptions> monitor,
        string name)
    {
        var current = provider.Current;
        var reference = new WeakReference(current);
        monitor.Set(name, new ReloadOptions());
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ResolveAndRemove(IKevlarRegistry registry, DisposableStrategy strategy)
    {
        var shield = registry.GetOrAdd("retired", _ => Shield.Use(strategy));
        var reference = new WeakReference(shield);
        registry.Remove("retired");
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ResolveAndRemove(
        IKevlarRegistry registry,
        string name,
        Strategy strategy)
    {
        var shield = registry.GetOrAdd(name, _ => Shield.Use(strategy));
        var reference = new WeakReference(shield);
        registry.Remove(name);
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task Execution, WeakReference Retired) StartBeforeRegistryResolution(
        IKevlarRegistry registry,
        Strategy strategy,
        TaskCompletionSource started,
        TaskCompletionSource release)
    {
        var shield = Shield.Use(strategy);
        var execution = shield.ExecuteAsync(
            (started, release),
            static async (state, _) =>
            {
                state.started.SetResult();
                await state.release.Task;
            }).AsTask();
        _ = registry.GetOrAdd("pre-resolution", _ => shield);
        var retired = new WeakReference(shield);
        _ = registry.Remove("pre-resolution");
        return (execution, retired);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ResolveAndRemove(
        IKevlarRegistry registry,
        DisposableStrategy shared,
        CallbackDisposableStrategy callback)
    {
        var shield = registry.GetOrAdd(
            "reentrant-retired",
            _ => Shield.Compose(Shield.Use(shared), Shield.Use(callback)));
        var reference = new WeakReference(shield);
        registry.Remove("reentrant-retired");
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Retired, WeakReference Released) ExerciseDerivedShield(
        IKevlarRegistry registry,
        DisposableStrategy strategy,
        Func<Shield, Shield> derive,
        int index)
    {
        var shield = registry.GetOrAdd("derived", _ => Shield.Use(strategy));
        var retired = new WeakReference(shield);
        var derived = derive(shield);
        registry.Remove("derived");

        shield = null!;
        Collect(retired);
        _ = registry.GetOrAdd($"derived-scavenge-{index}", _ => Shield.Empty);
        if (retired.IsAlive || strategy.DisposeCount != 0)
        {
            throw new InvalidOperationException("The derived shield did not preserve the retired strategy lifetime.");
        }

        if (derived.Execute(static _ => 42) != 42)
        {
            throw new InvalidOperationException("The derived shield returned an unexpected result.");
        }

        return (retired, new WeakReference(derived));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ClaimDisposableStrategy(StrategyDisposalTracker disposalTracker)
    {
        var strategy = new DisposableStrategy();
        _ = disposalTracker.TryClaim(strategy);
        return new WeakReference(strategy);
    }

    private static void Collect(WeakReference reference)
    {
        for (var attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class DisposableStrategy : Strategy, IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCount);

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private sealed class AsyncDisposableStrategy : Strategy, IAsyncDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return default;
        }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private sealed class ReentrantAsyncDisposableStrategy(Action callback) : Strategy, IAsyncDisposable
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task Disposed => _disposed.Task;

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            callback();
            Interlocked.Increment(ref _disposeCount);
            _disposed.TrySetResult();
        }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private sealed class BlockingAsyncDisposableStrategy : Strategy, IAsyncDisposable
    {
        private readonly TaskCompletionSource _disposalStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseDisposal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public Task DisposalStarted => _disposalStarted.Task;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public async ValueTask DisposeAsync()
        {
            _disposalStarted.TrySetResult();
            await _releaseDisposal.Task;
            Interlocked.Increment(ref _disposeCount);
        }

        public void ReleaseDisposal() => _releaseDisposal.TrySetResult();

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private sealed class CallbackDisposableStrategy(Action callback) : Strategy, IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            callback();
        }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private sealed class MutableOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        where TOptions : class, new()
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, TOptions> _values = new(StringComparer.Ordinal);
        private readonly List<Action<TOptions, string?>> _listeners = [];

        public TOptions CurrentValue => Get(Options.DefaultName);

        public TOptions Get(string? name)
        {
            lock (_sync)
            {
                return _values.TryGetValue(name ?? Options.DefaultName, out var value)
                    ? value
                    : new TOptions();
            }
        }

        public IDisposable OnChange(Action<TOptions, string?> listener)
        {
            lock (_sync)
            {
                _listeners.Add(listener);
            }

            return new Subscription(_sync, _listeners, listener);
        }

        public void Set(string name, TOptions value, bool notify = true)
        {
            Action<TOptions, string?>[] listeners;
            lock (_sync)
            {
                _values[name] = value;
                listeners = _listeners.ToArray();
            }

            if (notify)
            {
                foreach (var listener in listeners)
                {
                    listener(value, name);
                }
            }
        }

        private sealed class Subscription(
            object sync,
            List<Action<TOptions, string?>> listeners,
            Action<TOptions, string?> listener) : IDisposable
        {
            public void Dispose()
            {
                lock (sync)
                {
                    listeners.Remove(listener);
                }
            }
        }
    }

    private sealed class NullSubscriptionOptionsMonitor : IOptionsMonitor<ReloadOptions>
    {
        public ReloadOptions CurrentValue { get; } = new();

        public ReloadOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<ReloadOptions, string?> listener) => null!;
    }

    private sealed class ThrowingSubscriptionOptionsMonitor : IOptionsMonitor<ReloadOptions>
    {
        public ReloadOptions CurrentValue { get; } = new();

        public ReloadOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<ReloadOptions, string?> listener) => new ThrowingDisposable();

        private sealed class ThrowingDisposable : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("subscription cleanup failed");
        }
    }

    private sealed class ImmediateCallbackOptionsMonitor : IOptionsMonitor<ReloadOptions>
    {
        public ReloadOptions CurrentValue { get; } = new();

        public ReloadOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<ReloadOptions, string?> listener)
        {
            listener(CurrentValue, "immediate");
            return NullDisposable.Instance;
        }
    }
}

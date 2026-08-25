using System.Collections.Concurrent;
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
}

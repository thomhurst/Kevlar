using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class DependencyInjectionContractTests
{
    [Test]
    public async Task Concurrent_First_Resolution_Invokes_Factory_Exactly_Once()
    {
        const int workerCount = 32;
        var factoryCalls = 0;
        var services = new ServiceCollection();
        services.AddShield("shared", _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            Thread.SpinWait(500_000);
            return Shield.Empty;
        });
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();
        using var ready = new CountdownEvent(workerCount);
        using var start = new ManualResetEventSlim();

        var resolutions = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    start.Wait();
                    return registry.GetShield("shared");
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        ready.Wait();
        start.Set();
        var resolved = await Task.WhenAll(resolutions);

        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(resolved.All(shield => ReferenceEquals(shield, resolved[0]))).IsTrue();
    }

    [Test]
    public async Task Registry_TryGet_And_Keyed_Paths_Return_The_Same_Singletons()
    {
        var untyped = Shield.Retry(0, Backoff.None);
        var typed = Shield<int>.Empty;
        var services = new ServiceCollection()
            .AddShield("shared", _ => untyped)
            .AddShield("shared-typed", _ => typed)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();

        var directUntyped = registry.GetShield("shared");
        var foundUntyped = registry.TryGetShield("shared", out var triedUntyped);
        var keyedUntyped = services.GetRequiredKeyedService<Shield>("shared");
        var directTyped = registry.GetShield<int>("shared-typed");
        var foundTyped = registry.TryGetShield<int>("shared-typed", out var triedTyped);
        var keyedTyped = services.GetRequiredKeyedService<Shield<int>>("shared-typed");

        await Assert.That(foundUntyped).IsTrue();
        await Assert.That(foundTyped).IsTrue();
        await Assert.That(ReferenceEquals(directUntyped, untyped)).IsTrue();
        await Assert.That(ReferenceEquals(triedUntyped, untyped)).IsTrue();
        await Assert.That(ReferenceEquals(keyedUntyped, untyped)).IsTrue();
        await Assert.That(ReferenceEquals(directTyped, typed)).IsTrue();
        await Assert.That(ReferenceEquals(triedTyped, typed)).IsTrue();
        await Assert.That(ReferenceEquals(keyedTyped, typed)).IsTrue();
    }

    [Test]
    public async Task Fallback_Shield_Registry_And_Keyed_Paths_Return_The_Same_Singleton()
    {
        var expected = Shield.Fallback(static _ => ValueTask.CompletedTask);
        using var services = new ServiceCollection()
            .AddShield("void", expected)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();

        var direct = registry.GetShield("void");
        var found = registry.TryGetShield("void", out var tried);
        var keyed = services.GetRequiredKeyedService<Shield>("void");

        await Assert.That(found).IsTrue();
        await Assert.That(ReferenceEquals(direct, expected)).IsTrue();
        await Assert.That(ReferenceEquals(tried, expected)).IsTrue();
        await Assert.That(ReferenceEquals(keyed, expected)).IsTrue();
    }

    [Test]
    public async Task Duplicate_Name_Throws_Across_Shield_Shapes()
    {
        var first = Shield.Retry(1, Backoff.None);
        var services = new ServiceCollection().AddShield("duplicate", first);

        var sameShape = await Assert.That(() => services.AddShield(
                "duplicate",
                Shield.Timeout(TimeSpan.FromSeconds(1))))
            .Throws<InvalidOperationException>();
        var typedShape = await Assert.That(() => services.AddShield(
                "duplicate",
                Shield<int>.Empty))
            .Throws<InvalidOperationException>();

        await Assert.That(sameShape!.Message)
            .IsEqualTo("A shield named 'duplicate' is already registered.");
        await Assert.That(typedShape!.Message).IsEqualTo(sameShape.Message);
    }

    [Test]
    public async Task Replace_True_Overrides_Across_Shield_Shapes()
    {
        var replacement = Shield<int>.Empty;
        using var provider = new ServiceCollection()
            .AddShield("replace", Shield.Empty)
            .AddShield("replace", replacement, replace: true)
            .BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        await Assert.That(registry.TryGetShield("replace", out _)).IsFalse();
        await Assert.That(registry.GetShield<int>("replace")).IsSameReferenceAs(replacement);
        await Assert.That(provider.GetKeyedService<Shield>("replace")).IsNull();
        await Assert.That(provider.GetRequiredKeyedService<Shield<int>>("replace"))
            .IsSameReferenceAs(replacement);
    }

    [Test]
    public async Task AddKevlar_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddKevlar();
        services.AddKevlar();
        using var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetServices<IKevlarRegistry>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Names_Are_Case_Sensitive_And_May_Be_Empty()
    {
        using var provider = new ServiceCollection()
            .AddShield(string.Empty, Shield.Empty)
            .AddShield("Case", Shield.Retry(0, Backoff.None))
            .BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        await Assert.That(registry.GetShield(string.Empty)).IsNotNull();
        await Assert.That(registry.TryGetShield("case", out _)).IsFalse();
    }

    [Test]
    public async Task Registry_Name_Null_Guards_Report_Exact_Parameter()
    {
        using var provider = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        await AssertNullNameAsync(() => registry.GetShield(null!));
        await AssertNullNameAsync(() => registry.GetShield<int>(null!));
        await AssertNullNameAsync(() => registry.TryGetShield(null!, out _));
        await AssertNullNameAsync(() => registry.TryGetShield<int>(null!, out _));
        await AssertNullNameAsync(() => registry.GetOrAdd(null!, static _ => Shield.Empty));
        await AssertNullNameAsync(() => registry.GetOrAdd<int>(null!, static _ => Shield<int>.Empty));
        await AssertNullNameAsync(() => registry.TryAdd(null!, static _ => Shield.Empty));
        await AssertNullNameAsync(() => registry.TryAdd<int>(null!, static _ => Shield<int>.Empty));
        await AssertNullNameAsync(() => registry.Remove(null!));
        await AssertNullNameAsync(() => registry.Remove<int>(null!));
        await AssertNullParameterAsync(() => registry.GetOrAdd("name", null!), "factory");
        await AssertNullParameterAsync(() => registry.GetOrAdd<int>("name", null!), "factory");
        await AssertNullParameterAsync(() => registry.TryAdd("name", null!), "factory");
        await AssertNullParameterAsync(() => registry.TryAdd<int>("name", null!), "factory");
    }

    [Test]
    public async Task Registration_Overloads_Reject_Null_Inputs_With_Exact_Parameter_Names()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        Func<IServiceProvider, Shield> shieldFactory = static _ => Shield.Empty;
        Func<IServiceProvider, Shield<int>> typedFactory = static _ => Shield<int>.Empty;
        Func<IServiceProvider, string, Shield> partitionedFactory = static (_, _) => Shield.Empty;
        Func<IServiceProvider, string, Shield<int>> typedPartitionedFactory =
            static (_, _) => Shield<int>.Empty;
        Func<object, IServiceProvider, Shield> optionsFactory = static (_, _) => Shield.Empty;
        Func<object, IServiceProvider, Shield<int>> typedOptionsFactory =
            static (_, _) => Shield<int>.Empty;

        await AssertNullParameterAsync(
            () => KevlarServiceCollectionExtensions.AddKevlar(null!),
            "services");
        await AssertNullParameterAsync(
            () => KevlarServiceCollectionExtensions.AddShield(null!, "name", shieldFactory),
            "services");
        await AssertNullParameterAsync(() => services.AddShield(null!, shieldFactory), "name");
        await AssertNullParameterAsync(
            () => services.AddShield("name", (Func<IServiceProvider, Shield>)null!),
            "factory");
        await AssertNullParameterAsync(
            () => services.AddShield("name", (Shield)null!),
            "shield");
        await AssertNullParameterAsync(
            () => KevlarServiceCollectionExtensions.AddShield(null!, "name", configuration),
            "services");
        await AssertNullParameterAsync(
            () => services.AddShield("name", (IConfiguration)null!),
            "configuration");
        await AssertNullParameterAsync(
            () => services.AddShield<int>("name", (Func<IServiceProvider, Shield<int>>)null!),
            "factory");
        await AssertNullParameterAsync(
            () => services.AddShield<int>("name", (Shield<int>)null!),
            "shield");
        await AssertNullParameterAsync(
            () => services.AddShield<int>("name", (IConfiguration)null!),
            "configuration");
        await AssertNullParameterAsync(
            () => KevlarServiceCollectionExtensions.AddShield<int>(null!, "name", typedFactory),
            "services");
        await AssertNullParameterAsync(() => services.AddShield<int>(null!, typedFactory), "name");
        await AssertNullParameterAsync(
            () => KevlarServiceCollectionExtensions.AddReloadingShield<object>(
                null!,
                "name",
                optionsFactory),
            "services");
        await AssertNullParameterAsync(
            () => services.AddReloadingShield<object>(null!, optionsFactory),
            "name");
        await AssertNullParameterAsync(
            () => services.AddReloadingShield<object>(
                "name",
                (Func<object, IServiceProvider, Shield>)null!),
            "build");
        await AssertNullParameterAsync(
            () => KevlarServiceCollectionExtensions.AddReloadingShield<object, int>(
                null!,
                "name",
                typedOptionsFactory),
            "services");
        await AssertNullParameterAsync(
            () => services.AddReloadingShield<object, int>(null!, typedOptionsFactory),
            "name");
        await AssertNullParameterAsync(
            () => services.AddReloadingShield<object, int>("name", null!),
            "build");
        await AssertNullParameterAsync(
            () => KevlarServiceCollectionExtensions.AddPartitionedShield(
                null!,
                "name",
                partitionedFactory),
            "services");
        await AssertNullParameterAsync(
            () => services.AddPartitionedShield<string>(null!, partitionedFactory),
            "name");
        await AssertNullParameterAsync(
            () => services.AddPartitionedShield<string>("name", null!),
            "factory");
        await AssertNullParameterAsync(
            () => KevlarServiceCollectionExtensions.AddPartitionedShield(
                null!,
                "name",
                typedPartitionedFactory),
            "services");
        await AssertNullParameterAsync(
            () => services.AddPartitionedShield<string, int>(null!, typedPartitionedFactory),
            "name");
        await AssertNullParameterAsync(
            () => services.AddPartitionedShield<string, int>("name", null!),
            "factory");
    }

    [Test]
    public async Task Ordinary_Shield_Exposes_A_Fixed_Keyed_Provider()
    {
        using var services = new ServiceCollection()
            .AddShield("static", Shield.Empty)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("static");

        await Assert.That(ReferenceEquals(shieldProvider.Current, registry.GetShield("static"))).IsTrue();
    }

    [Test]
    public async Task Typed_Ordinary_Shield_Exposes_A_Fixed_Keyed_Provider()
    {
        using var services = new ServiceCollection()
            .AddShield("static", Shield<int>.Empty)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider<int>>("static");

        await Assert.That(ReferenceEquals(shieldProvider.Current, registry.GetShield<int>("static")))
            .IsTrue();
    }

    [Test]
    public async Task Missing_Typed_Registration_Reports_Name_And_Result_Type()
    {
        using var provider = new ServiceCollection().AddKevlar().BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        var error = await Assert.That(() => registry.GetShield<int>("missing"))
            .Throws<KeyNotFoundException>();

        await Assert.That(error!.Message).Contains("missing");
        await Assert.That(error.Message).Contains("Int32");
    }

    [Test]
    public async Task Factory_Failure_Is_Retried_On_The_Next_Resolve()
    {
        var factoryCalls = 0;
        var failure = new InvalidOperationException("factory failed");
        using var provider = new ServiceCollection()
            .AddShield("failing", _ =>
            {
                factoryCalls++;
                if (factoryCalls == 1)
                {
                    throw failure;
                }

                return Shield.Empty;
            })
            .BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        var first = await Assert.That(() => registry.GetShield("failing")).Throws<InvalidOperationException>();
        var second = registry.GetShield("failing");

        await Assert.That(factoryCalls).IsEqualTo(2);
        await Assert.That(ReferenceEquals(first, failure)).IsTrue();
        await Assert.That(second).IsSameReferenceAs(Shield.Empty);
    }

    [Test]
    public async Task Concurrent_Failed_Resolve_Uses_One_Factory_Attempt()
    {
        const int WorkerCount = 32;
        var factoryCalls = 0;
        var failure = new InvalidOperationException("factory failed");
        using var releaseFailure = new ManualResetEventSlim();
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var provider = new ServiceCollection()
            .AddShield("failing", _ =>
            {
                if (Interlocked.Increment(ref factoryCalls) == 1)
                {
                    factoryEntered.SetResult();
                    releaseFailure.Wait();
                    throw failure;
                }

                return Shield.Empty;
            })
            .BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();
        using var ready = new CountdownEvent(WorkerCount);
        using var start = new ManualResetEventSlim();
        var resolutions = Enumerable.Range(0, WorkerCount)
            .Select(_worker => Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    start.Wait();
                    try
                    {
                        _ = registry.GetShield("failing");
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        ready.Wait();
        start.Set();
        await factoryEntered.Task;
        await Task.Delay(100);
        releaseFailure.Set();
        var failures = await Task.WhenAll(resolutions);

        await Assert.That(factoryCalls).IsEqualTo(1);
        await Assert.That(failures.All(exception => ReferenceEquals(exception, failure))).IsTrue();
        await Assert.That(registry.GetShield("failing")).IsSameReferenceAs(Shield.Empty);
        await Assert.That(factoryCalls).IsEqualTo(2);
    }

    [Test]
    public async Task Full_Definition_Uses_The_Documented_Order()
    {
        var definition = new ShieldDefinition
        {
            Timeout = TimeSpan.FromSeconds(30),
            Retry = new RetryDefinition { MaxRetries = 2, Backoff = BackoffKind.None },
            CircuitBreaker = new CircuitBreakerDefinition
            {
                ConsecutiveFailures = 3,
                BreakDuration = TimeSpan.FromSeconds(4),
            },
            RateLimit = new RateLimitDefinition
            {
                Permits = 5,
                Window = TimeSpan.FromSeconds(10),
                Burst = 7,
                QueueLimit = 2,
            },
            ConcurrencyLimit = new ConcurrencyLimitDefinition { MaxConcurrency = 3, QueueLimit = 4 },
            AttemptTimeout = TimeSpan.FromSeconds(1),
        };

        await Assert.That(definition.Build().ToString()).IsEqualTo(
            "Timeout(30s) → Retry(2, no delay) → CircuitBreaker(3 consecutive, break 4s) → " +
            "RateLimit(5/10s, burst 7, queue 2) → ConcurrencyLimit(3, queue 4) → Timeout(1s)");
    }

    [Test]
    [Arguments(BackoffKind.None, "no delay, ≤4s")]
    [Arguments(BackoffKind.Constant, "constant 2s, ≤4s")]
    [Arguments(BackoffKind.Linear, "linear 2s steps, cap 4s")]
    [Arguments(BackoffKind.Exponential, "exponential 2s ×3, cap 4s")]
    public async Task Every_BackoffKind_Binds_All_Applicable_Knobs(BackoffKind kind, string expected)
    {
        var definition = new ShieldDefinition
        {
            Retry = new RetryDefinition
            {
                MaxRetries = 1,
                Backoff = kind,
                BaseDelay = TimeSpan.FromSeconds(2),
                Factor = 3,
                Jitter = Jitter.None,
                MaxDelay = TimeSpan.FromSeconds(4),
            },
        };

        await Assert.That(definition.Build().ToString()).IsEqualTo($"Retry(1, {expected})");
    }

    [Test]
    public async Task Invalid_BackoffKind_Fails_Actionably()
    {
        var definition = new ShieldDefinition
        {
            Retry = new RetryDefinition { Backoff = (BackoffKind)int.MaxValue },
        };

        var error = await Assert.That(() => definition.Build()).Throws<ArgumentOutOfRangeException>();
        await Assert.That(error!.ParamName).IsEqualTo("Backoff");
    }

    [Test]
    public async Task Custom_BackoffKind_Requires_The_Fluent_Api()
    {
        var definition = new ShieldDefinition
        {
            Retry = new RetryDefinition { Backoff = BackoffKind.Custom },
        };

        await Assert.That(() => definition.Build())
            .Throws<InvalidOperationException>()
            .WithMessage(
                "RetryDefinition cannot construct BackoffKind.Custom; configure Backoff.Custom "
                + "with the fluent API.");
    }

    [Test]
    public async Task Configuration_Is_Read_On_First_Resolution_Then_Remains_Stable()
    {
        var values = new Dictionary<string, string?>
        {
            ["Retry:MaxRetries"] = "1",
            ["Retry:Backoff"] = "None",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddShield("dynamic", configuration);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        configuration["Retry:MaxRetries"] = "4";
        var first = registry.GetShield("dynamic");
        configuration["Retry:MaxRetries"] = "2";
        var second = registry.GetShield("dynamic");

        await Assert.That(first.Name).IsEqualTo("dynamic");
        await Assert.That(first.ToString()).IsEqualTo("dynamic: Retry(4, no delay)");
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(second.ToString()).IsEqualTo("dynamic: Retry(4, no delay)");
    }

    [Test]
    public async Task Invalid_Configuration_Reports_Section_And_Property()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:Retry:MaxRetries"] = "-1",
            })
            .Build()
            .GetSection("Resilience");
        var services = new ServiceCollection();
        services.AddShield("invalid", configuration);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        var exception = await Assert.That(() => registry.GetShield("invalid"))
            .Throws<KevlarConfigurationException>();

        await Assert.That(exception!.Message).Contains("Resilience");
        await Assert.That(exception.Message).Contains("RetryOptions.MaxRetries");
        await Assert.That(exception.Message).Contains("-1");
        await Assert.That(exception.InnerException).IsTypeOf<KevlarConfigurationException>();
    }

    [Test]
    public async Task Bound_Validation_Failures_Report_Configuration_Path()
    {
        (string Key, string Value, bool Typed)[] cases =
        [
            ("Timeout", "00:00:00", false),
            ("AttemptTimeout", "00:00:00", true),
            ("ConcurrencyLimit:MaxConcurrency", "0", false),
            ("Retry:BaseDelay", "-00:00:01", false),
            ("Retry:MaxRetries", "abc", true),
            ("ConcurrencyLimit:MaxQueue", "5", false),
        ];

        foreach (var item in cases)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"Resilience:{item.Key}"] = item.Value,
                })
                .Build()
                .GetSection("Resilience");
            var services = new ServiceCollection();
            if (item.Typed)
            {
                services.AddShield<int>("invalid", configuration);
            }
            else
            {
                services.AddShield("invalid", configuration);
            }

            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<IKevlarRegistry>();
            var exception = item.Typed
                ? await Assert.That(() => registry.GetShield<int>("invalid"))
                    .Throws<KevlarConfigurationException>()
                : await Assert.That(() => registry.GetShield("invalid"))
                    .Throws<KevlarConfigurationException>();

            await Assert.That(exception!.Message).Contains("Resilience");
            await Assert.That(exception.InnerException).IsTypeOf<KevlarConfigurationException>();
        }
    }

    private static async Task AssertNullNameAsync(Action action)
    {
        await AssertNullParameterAsync(action, "name");
    }

    private static async Task AssertNullParameterAsync(Action action, string parameterName)
    {
        var error = await Assert.That(action).Throws<ArgumentNullException>();
        await Assert.That(error!.ParamName).IsEqualTo(parameterName);
    }
}

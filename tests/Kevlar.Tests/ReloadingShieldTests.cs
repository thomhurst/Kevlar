using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class ReloadingShieldTests
{
    private static ReloadingShieldOptions ImmediateReload => new()
    {
        DebounceDelay = TimeSpan.Zero,
    };

    [Test]
    public async Task Registration_Null_Guards_Report_Exact_Parameters()
    {
        var configuration = BuildConfiguration();
        IServiceCollection? missingServices = null;

        var servicesError = await Assert.That(() => missingServices!.AddReloadingShield("dynamic", configuration))
            .Throws<ArgumentNullException>();
        var nameError = await Assert.That(() => new ServiceCollection().AddReloadingShield(null!, configuration))
            .Throws<ArgumentNullException>();
        var configurationError = await Assert.That(() => new ServiceCollection().AddReloadingShield("dynamic", null!))
            .Throws<ArgumentNullException>();
        var typedServicesError = await Assert.That(
                () => missingServices!.AddReloadingShield<int>("dynamic", configuration))
            .Throws<ArgumentNullException>();
        var typedNameError = await Assert.That(
                () => new ServiceCollection().AddReloadingShield<int>(null!, configuration))
            .Throws<ArgumentNullException>();
        var typedConfigurationError = await Assert.That(
                () => new ServiceCollection().AddReloadingShield<int>("dynamic", null!))
            .Throws<ArgumentNullException>();
        var optionsError = await Assert.That(
                () => new ServiceCollection().AddReloadingShield(
                    "dynamic",
                    (ReloadingShieldOptions)null!,
                    configuration))
            .Throws<ArgumentNullException>();

        await Assert.That(servicesError!.ParamName).IsEqualTo("services");
        await Assert.That(nameError!.ParamName).IsEqualTo("name");
        await Assert.That(configurationError!.ParamName).IsEqualTo("configuration");
        await Assert.That(typedServicesError!.ParamName).IsEqualTo("services");
        await Assert.That(typedNameError!.ParamName).IsEqualTo("name");
        await Assert.That(typedConfigurationError!.ParamName).IsEqualTo("configuration");
        await Assert.That(optionsError!.ParamName).IsEqualTo("options");
    }

    [Test]
    public async Task Reload_Options_Reject_Invalid_Values()
    {
        var configuration = BuildConfiguration();
        var negativeDelay = new ReloadingShieldOptions { DebounceDelay = TimeSpan.FromTicks(-1) };
        var excessiveDelay = new ReloadingShieldOptions { DebounceDelay = TimeSpan.FromDays(50) };
        var missingClock = new ReloadingShieldOptions { TimeProvider = null! };

        var delayError = await Assert.That(() => new ServiceCollection().AddReloadingShield(
                "dynamic",
                negativeDelay,
                configuration))
            .Throws<ArgumentOutOfRangeException>();
        var clockError = await Assert.That(() => new ServiceCollection().AddReloadingShield(
                "dynamic",
                missingClock,
                configuration))
            .Throws<ArgumentException>();
        var excessiveDelayError = await Assert.That(() => new ServiceCollection().AddReloadingShield(
                "dynamic",
                excessiveDelay,
                configuration))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(delayError!.ParamName).IsEqualTo("options");
        await Assert.That(excessiveDelayError!.ParamName).IsEqualTo("options");
        await Assert.That(clockError!.ParamName).IsEqualTo("options");
    }

    [Test]
    public async Task Valid_Reload_Atomically_Publishes_A_Fresh_Snapshot()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"), ("Retry:Backoff", "None"));
        using var services = new ServiceCollection()
            .AddReloadingShield("dynamic", ImmediateReload, configuration)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var first = shieldProvider.Current;

        configuration["Retry:MaxRetries"] = "4";
        configuration.Reload();

        var second = shieldProvider.Current;
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        await Assert.That(first.ToString()).IsEqualTo("dynamic: Retry(1, no delay)");
        await Assert.That(second.ToString()).IsEqualTo("dynamic: Retry(4, no delay)");
        await Assert.That(ReferenceEquals(registry.GetShield("dynamic"), second)).IsTrue();
    }

    [Test]
    public async Task Reloads_Are_Debounced_And_Later_Windows_Rebuild_Again()
    {
        var timeProvider = new FakeTimeProvider();
        var options = new ReloadingShieldOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(250),
            TimeProvider = timeProvider,
        };
        var configuration = BuildConfiguration(("Retry:MaxRetries", "0"), ("Retry:Backoff", "None"));
        using var services = new ServiceCollection()
            .AddReloadingShield("dynamic", options, configuration)
            .BuildServiceProvider();
        var provider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var initial = provider.Current;

        configuration["Retry:MaxRetries"] = "1";
        configuration.Reload();
        configuration["Retry:MaxRetries"] = "2";
        configuration.Reload();

        await Assert.That(provider.Current).IsSameReferenceAs(initial);
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        var firstReload = provider.Current;
        await Assert.That(firstReload.ToString()).IsEqualTo("dynamic: Retry(2, no delay)");

        configuration["Retry:MaxRetries"] = "3";
        configuration.Reload();
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));

        await Assert.That(provider.Current).IsNotSameReferenceAs(firstReload);
        await Assert.That(provider.Current.ToString()).IsEqualTo("dynamic: Retry(3, no delay)");
    }

    [Test]
    public async Task Typed_Reload_Rebuilds_And_Resets_Strategy_State()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "0"),
            ("Retry:Backoff", "None"),
            ("CircuitBreaker:ConsecutiveFailures", "1"),
            ("CircuitBreaker:FailureRatio", ""),
            ("CircuitBreaker:BreakDuration", "01:00:00"));
        using var services = new ServiceCollection()
            .AddReloadingShield<HttpResponseMessage>("dynamic", ImmediateReload, configuration)
            .BuildServiceProvider();
        var registry = services.GetRequiredService<IKevlarRegistry>();
        var live = services.GetRequiredKeyedService<IShieldProvider<HttpResponseMessage>>("dynamic");
        var first = live.Current;
        var attempts = 0;

        await Assert.That(async () => await first.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException("failure");
        })).Throws<InvalidOperationException>();
        await Assert.That(async () => await first.ExecuteAsync(_ =>
        {
            attempts++;
            return new ValueTask<HttpResponseMessage>(new HttpResponseMessage());
        })).Throws<CircuitOpenException>();

        configuration["Retry:MaxRetries"] = "4";
        configuration.Reload();

        var second = live.Current;
        using var response = await second.ExecuteAsync(_ =>
        {
            attempts++;
            return new ValueTask<HttpResponseMessage>(new HttpResponseMessage());
        });

        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        await Assert.That(ReferenceEquals(registry.GetShield<HttpResponseMessage>("dynamic"), second))
            .IsTrue();
        await Assert.That(first.ToString()).Contains("Retry(0, no delay)");
        await Assert.That(second.ToString()).Contains("Retry(4, no delay)");
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_Invalid_Reload_Keeps_Last_Good_And_Reports_Path()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"));
        Exception? reported = null;
        using var services = new ServiceCollection()
            .AddReloadingShield<int>(
                "dynamic",
                ImmediateReload,
                configuration,
                error => reported = error)
            .BuildServiceProvider();
        var live = services.GetRequiredKeyedService<IShieldProvider<int>>("dynamic");
        var lastGood = live.Current;

        configuration["Retry:MaxRetries"] = "invalid";
        configuration.Reload();

        await Assert.That(ReferenceEquals(live.Current, lastGood)).IsTrue();
        await Assert.That(reported).IsTypeOf<KevlarConfigurationException>();
        await Assert.That(reported!.Message).Contains("Retry:MaxRetries");
        await Assert.That(reported.Message).Contains("not an integer");
    }

    [Test]
    public async Task Reloading_Name_Registers_Only_The_Live_Keyed_Provider()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"), ("Retry:Backoff", "None"));
        using var services = new ServiceCollection()
            .AddReloadingShield("dynamic", ImmediateReload, configuration)
            .BuildServiceProvider();
        var live = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var keyedShield = services.GetKeyedService<Shield>("dynamic");

        configuration["Retry:MaxRetries"] = "2";
        configuration.Reload();

        await Assert.That(live.Current.ToString()).IsEqualTo("dynamic: Retry(2, no delay)");
        await Assert.That(keyedShield).IsNull();
    }

    [Test]
    public async Task Last_Mixed_Registration_Wins_For_Registry_And_Provider()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"), ("Retry:Backoff", "None"));
        var fixedShield = Shield.Timeout(TimeSpan.FromSeconds(1));
        using var fixedLast = new ServiceCollection()
            .AddReloadingShield("dynamic", ImmediateReload, configuration)
            .AddShield("dynamic", fixedShield, replace: true)
            .BuildServiceProvider();
        using var reloadingLast = new ServiceCollection()
            .AddShield("dynamic", fixedShield)
            .AddReloadingShield(
                "dynamic",
                ImmediateReload,
                configuration,
                replace: true)
            .BuildServiceProvider();

        var fixedRegistry = fixedLast.GetRequiredService<IKevlarRegistry>();
        var fixedProvider = fixedLast.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var reloadingRegistry = reloadingLast.GetRequiredService<IKevlarRegistry>();
        var reloadingProvider = reloadingLast.GetRequiredKeyedService<IShieldProvider>("dynamic");

        await Assert.That(ReferenceEquals(fixedRegistry.GetShield("dynamic"), fixedShield)).IsTrue();
        await Assert.That(ReferenceEquals(fixedProvider.Current, fixedShield)).IsTrue();
        await Assert.That(reloadingRegistry.GetShield("dynamic").ToString())
            .IsEqualTo("dynamic: Retry(1, no delay)");
        await Assert.That(reloadingProvider.Current.ToString())
            .IsEqualTo("dynamic: Retry(1, no delay)");
    }

    [Test]
    public async Task Invalid_Reload_Keeps_Last_Good_And_Reports_The_Full_Key()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"), ("Retry:Backoff", "None"));
        Exception? reported = null;
        using var services = new ServiceCollection()
            .AddReloadingShield(
                "dynamic",
                ImmediateReload,
                configuration,
                error => reported = error)
            .BuildServiceProvider();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var lastGood = shieldProvider.Current;

        configuration["Retry:MaxRetries"] = "invalid";
        configuration.Reload();

        await Assert.That(ReferenceEquals(shieldProvider.Current, lastGood)).IsTrue();
        await Assert.That(reported).IsTypeOf<KevlarConfigurationException>();
        await Assert.That(reported!.Message).Contains("Retry:MaxRetries");
        await Assert.That(reported.Message).Contains("not an integer");
    }

    [Test]
    public async Task Callback_Failure_Does_Not_Stop_Subsequent_Reloads()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"), ("Retry:Backoff", "None"));
        using var services = new ServiceCollection()
            .AddReloadingShield(
                "dynamic",
                ImmediateReload,
                configuration,
                _ => throw new InvalidOperationException("callback"))
            .BuildServiceProvider();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        _ = shieldProvider.Current;

        configuration["Retry:MaxRetries"] = "invalid";
        configuration.Reload();
        configuration["Retry:MaxRetries"] = "3";
        configuration.Reload();

        await Assert.That(shieldProvider.Current.ToString()).IsEqualTo("dynamic: Retry(3, no delay)");
    }

    [Test]
    public async Task Repeated_Reloads_Publish_Complete_Snapshots_Under_Concurrent_Reads()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "0"), ("Retry:Backoff", "None"));
        using var services = new ServiceCollection()
            .AddReloadingShield("dynamic", ImmediateReload, configuration)
            .BuildServiceProvider();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var unexpected = new List<string>();
        using var stop = new CancellationTokenSource();
        var readers = Enumerable.Range(0, Environment.ProcessorCount)
            .Select(_ => Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var description = shieldProvider.Current.ToString();
                    if (description is not "dynamic: Retry(0, no delay)" and
                        not "dynamic: Retry(1, no delay)" and
                        not "dynamic: Retry(2, no delay)")
                    {
                        lock (unexpected)
                        {
                            unexpected.Add(description);
                        }
                    }
                }
            }))
            .ToArray();

        for (var reload = 0; reload < 30; reload++)
        {
            configuration["Retry:MaxRetries"] = (reload % 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
            configuration.Reload();
        }

        stop.Cancel();
        await Task.WhenAll(readers);

        await Assert.That(unexpected).IsEmpty();
    }

    [Test]
    public async Task Concurrent_First_Resolve_And_Reload_Cannot_Miss_The_Change()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var configuration = BuildConfiguration(("Retry:MaxRetries", "0"), ("Retry:Backoff", "None"));
            using var services = new ServiceCollection()
                .AddReloadingShield("dynamic", ImmediateReload, configuration)
                .BuildServiceProvider();
            var resolve = Task.Run(() => services.GetRequiredKeyedService<IShieldProvider>("dynamic"));

            configuration["Retry:MaxRetries"] = "1";
            configuration.Reload();
            var shieldProvider = await resolve;

            await Assert.That(shieldProvider.Current.ToString())
                .IsEqualTo("dynamic: Retry(1, no delay)");
        }
    }

    [Test]
    public async Task In_Flight_Execution_Finishes_On_The_Snapshot_It_Started_With()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"), ("Retry:Backoff", "None"));
        using var services = new ServiceCollection()
            .AddReloadingShield("dynamic", ImmediateReload, configuration)
            .BuildServiceProvider();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var oldSnapshot = shieldProvider.Current;
        var attempts = 0;
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = oldSnapshot.ExecuteAsync(async _ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstAttempt.SetResult();
                await continueAttempt.Task;
            }

            throw new InvalidOperationException("retry");
        }).AsTask();

        await firstAttempt.Task;
        configuration["Retry:MaxRetries"] = "4";
        configuration.Reload();
        continueAttempt.SetResult();

        await Assert.That(async () => await execution).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(ReferenceEquals(shieldProvider.Current, oldSnapshot)).IsFalse();
    }

    [Test]
    public async Task Successful_Reload_Starts_With_Fresh_Strategy_State()
    {
        var configuration = BuildConfiguration(
            ("CircuitBreaker:ConsecutiveFailures", "1"),
            ("CircuitBreaker:BreakDuration", "01:00:00"));
        using var services = new ServiceCollection()
            .AddReloadingShield("dynamic", ImmediateReload, configuration)
            .BuildServiceProvider();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var attempts = 0;

        await Assert.That(async () => await shieldProvider.Current.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException("failure");
        })).Throws<InvalidOperationException>();
        await Assert.That(async () => await shieldProvider.Current.ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.CompletedTask;
        })).Throws<CircuitOpenException>();

        configuration.Reload();

        await Assert.That(async () => await shieldProvider.Current.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException("failure");
        })).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Disposal_Unsubscribes_From_Configuration_Changes()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"), ("Retry:Backoff", "None"));
        var timeProvider = new FakeTimeProvider();
        var options = new ReloadingShieldOptions
        {
            DebounceDelay = TimeSpan.FromMilliseconds(250),
            TimeProvider = timeProvider,
        };
        var failures = 0;
        var services = new ServiceCollection()
            .AddReloadingShield(
                "dynamic",
                options,
                configuration,
                _ => Interlocked.Increment(ref failures))
            .BuildServiceProvider();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var snapshot = shieldProvider.Current;

        configuration["Retry:MaxRetries"] = "2";
        configuration.Reload();
        services.Dispose();
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        configuration["Retry:MaxRetries"] = "invalid";
        configuration.Reload();

        await Assert.That(failures).IsEqualTo(0);
        await Assert.That(ReferenceEquals(shieldProvider.Current, snapshot)).IsTrue();
    }

    [Test]
    public async Task Provider_Current_Read_Does_Not_Allocate()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "1"), ("Retry:Backoff", "None"));
        using var services = new ServiceCollection()
            .AddReloadingShield("dynamic", ImmediateReload, configuration)
            .BuildServiceProvider();
        var shieldProvider = services.GetRequiredKeyedService<IShieldProvider>("dynamic");
        const int Iterations = 10_000;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            _ = shieldProvider.Current;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            _ = shieldProvider.Current;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    private static IConfigurationRoot BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

}

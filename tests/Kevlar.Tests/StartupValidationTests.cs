using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kevlar.Tests;

public class StartupValidationTests
{
    [Test]
    public async Task Successful_Startup_Uses_Cached_Shields_And_Disposes_Resources_Once()
    {
        var untypedCalls = 0;
        var typedCalls = 0;
        var strategy = new CountingStrategy();
        using var host = CreateHost(services =>
        {
            services.AddKevlarValidationOnStart();
            services.AddShield("untyped", _ =>
            {
                untypedCalls++;
                return Shield.Use(strategy);
            });
            services.AddShield<int>("typed", _ =>
            {
                typedCalls++;
                return Shield.For<int>().Retry(1, Backoff.None);
            });
            services.AddKevlarValidationOnStart();
        });

        await Assert.That(untypedCalls + typedCalls).IsEqualTo(0);
        await host.StartAsync();
        var registry = host.Services.GetRequiredService<IKevlarRegistry>();
        var untyped = registry.GetShield("untyped");
        var typed = registry.GetShield<int>("typed");

        await Assert.That(host.Services.GetRequiredKeyedService<Shield>("untyped")).IsSameReferenceAs(untyped);
        await Assert.That(host.Services.GetRequiredKeyedService<Shield<int>>("typed")).IsSameReferenceAs(typed);
        await Assert.That(untypedCalls).IsEqualTo(1);
        await Assert.That(typedCalls).IsEqualTo(1);
        await Assert.That(strategy.Executions).IsEqualTo(0);
        await Assert.That(strategy.Disposals).IsEqualTo(0);

        await host.StopAsync();
        host.Dispose();

        await Assert.That(strategy.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Factory_Failure_Reports_Name_And_Original_Error_Before_Workers_Start(bool typed)
    {
        var failure = new InvalidOperationException("factory unavailable");
        var worker = new ObservingWorker();
        using var host = CreateHost(services =>
        {
            services.AddSingleton<IHostedService>(worker);
            if (typed)
            {
                services.AddShield<int>("broken", (IServiceProvider _) => throw failure);
            }
            else
            {
                services.AddShield("broken", (IServiceProvider _) => throw failure);
            }
            services.AddKevlarValidationOnStart();
        });

        var error = await Assert.That(async () => await host.StartAsync()).Throws<KevlarConfigurationException>();

        await Assert.That(error!.Message).Contains("broken");
        await Assert.That(error.Message).Contains("factory unavailable");
        await Assert.That(error.InnerException).IsSameReferenceAs(failure);
        await Assert.That(worker.Started).IsFalse();
        if (typed)
        {
            await Assert.That(error.Message).Contains("System.Int32");
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Invalid_Configuration_Fails_Startup(bool typed, bool reloading)
    {
        var configuration = Configuration("-1");
        using var host = CreateHost(services =>
        {
            var section = configuration.GetSection("Resilience");
            if (reloading)
            {
                if (typed) { services.AddReloadingShield<int>("configured", section); }
                else { services.AddReloadingShield("configured", section); }
            }
            else
            {
                if (typed) { services.AddShield<int>("configured", section); }
                else { services.AddShield("configured", section); }
            }
            services.AddKevlarValidationOnStart();
        });

        var error = await Assert.That(async () => await host.StartAsync()).Throws<KevlarConfigurationException>();

        await Assert.That(error!.Message).Contains("configured");
        await Assert.That(error.Message).Contains("Resilience");
        await Assert.That(error.Message).Contains("MaxRetries");
    }

    [Test]
    public async Task Reloading_Startup_Uses_One_Publication_And_Later_Invalid_Reload_Keeps_It()
    {
        var configuration = Configuration("1");
        var failures = new List<Exception>();
        var decorationCount = 0;
        using var host = CreateHost(services =>
        {
            services.AddSingleton<IShieldDecorator>(new CountingDecorator(() => decorationCount++));
            var options = new ReloadingShieldOptions { DebounceDelay = TimeSpan.Zero };
            services.AddReloadingShield("untyped", options, configuration.GetSection("Resilience"), failures.Add);
            services.AddReloadingShield<int>("typed", options, configuration.GetSection("Resilience"), failures.Add);
            services.AddKevlarValidationOnStart();
        });

        await host.StartAsync();
        await Assert.That(decorationCount).IsEqualTo(2);
        var untyped = host.Services.GetRequiredKeyedService<IShieldProvider>("untyped");
        var typed = host.Services.GetRequiredKeyedService<IShieldProvider<int>>("typed");
        var firstUntyped = untyped.Current;
        var firstTyped = typed.Current;
        var registry = host.Services.GetRequiredService<IKevlarRegistry>();
        _ = registry.GetShield("untyped");
        _ = registry.GetShield<int>("typed");

        configuration["Resilience:Retry:MaxRetries"] = "-1";
        configuration.Reload();

        await Assert.That(untyped.Current).IsSameReferenceAs(firstUntyped);
        await Assert.That(typed.Current).IsSameReferenceAs(firstTyped);
        await Assert.That(decorationCount).IsEqualTo(2);
        await Assert.That(failures.Count).IsEqualTo(2);
        await host.StopAsync();
    }

    [Test]
    public async Task Disabled_Validation_Preserves_Lazy_Factory_Failures()
    {
        var calls = 0;
        using var host = CreateHost(services => services.AddShield("lazy", (IServiceProvider _) =>
        {
            calls++;
            throw new InvalidOperationException("deferred");
        }));

        await host.StartAsync();

        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(() => host.Services.GetRequiredService<IKevlarRegistry>().GetShield("lazy"))
            .Throws<InvalidOperationException>();
        await Assert.That(calls).IsEqualTo(1);
        await host.StopAsync();
    }

    [Test]
    public async Task Named_Options_Reloading_Factories_Run_Once_During_Startup()
    {
        var calls = 0;
        using var host = CreateHost(services =>
        {
            services.Configure<OtherOptions>("untyped", _ => { });
            services.Configure<OtherOptions>("typed", _ => { });
            services.AddReloadingShield<OtherOptions>("untyped", (_, _) =>
            {
                calls++;
                return Shield.Retry(1, Backoff.None);
            });
            services.AddReloadingShield<OtherOptions, int>("typed", (_, _) =>
            {
                calls++;
                return Shield.For<int>().Retry(1, Backoff.None);
            });
            services.AddKevlarValidationOnStart();
        });

        await host.StartAsync();
        await Assert.That(calls).IsEqualTo(2);
        var registry = host.Services.GetRequiredService<IKevlarRegistry>();
        _ = registry.GetShield("untyped");
        _ = registry.GetShield<int>("typed");
        await Assert.That(calls).IsEqualTo(2);
        await host.StopAsync();
    }

    [Test]
    public async Task Startup_Skips_Runtime_Additions_And_Partition_Keys_And_Uses_Replacements()
    {
        var replacedCalls = 0;
        var partitionCalls = 0;
        var dynamicCalls = 0;
        using var host = CreateHost(services =>
        {
            services.AddShield("replace", _ => { replacedCalls++; return Shield.Empty; });
            services.AddShield("replace", Shield.Retry(2, Backoff.None), replace: true);
            services.AddShield("removed", (IServiceProvider _) => throw new InvalidOperationException("removed"));
            services.AddPartitionedShield<string>("tenants", (_, _) => { partitionCalls++; return Shield.Empty; });
            services.AddKevlarValidationOnStart();
        });
        var registry = host.Services.GetRequiredService<IKevlarRegistry>();
        registry.TryAdd("runtime", _ => { dynamicCalls++; return Shield.Empty; });
        registry.Remove("removed");

        await host.StartAsync();

        await Assert.That(replacedCalls + partitionCalls + dynamicCalls).IsEqualTo(0);
        await Assert.That(registry.GetShield("replace").ToString()).Contains("Retry(2, no delay)");
        await host.StopAsync();
    }

    [Test]
    public async Task Failed_Startup_Still_Disposes_Previously_Constructed_Strategies()
    {
        var strategy = new CountingStrategy();
        using var host = CreateHost(services =>
        {
            services.AddShield("valid", _ => Shield.Use(strategy));
            services.AddShield("invalid", (IServiceProvider _) => throw new InvalidOperationException("broken"));
            services.AddKevlarValidationOnStart();
        });

        await Assert.That(async () => await host.StartAsync()).Throws<KevlarConfigurationException>();
        host.Dispose();

        await Assert.That(strategy.Disposals).IsEqualTo(1);
        await Assert.That(strategy.Executions).IsEqualTo(0);
    }

    [Test]
    public async Task Empty_Registration_Set_Coexists_With_Other_Startup_Validators()
    {
        using var host = CreateHost(services =>
        {
            services.AddKevlarValidationOnStart();
            services.AddOptions<OtherOptions>().Validate(_ => false, "other invalid").ValidateOnStart();
        });

        await Assert.That(async () => await host.StartAsync()).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Null_Services_Are_Rejected()
    {
        var error = await Assert.That(() => KevlarServiceCollectionExtensions.AddKevlarValidationOnStart(null!))
            .Throws<ArgumentNullException>();
        await Assert.That(error!.ParamName).IsEqualTo("services");
    }

    private static IHost CreateHost(Action<IServiceCollection> configure) =>
        new HostBuilder().ConfigureServices(configure).Build();

    private static IConfigurationRoot Configuration(string maxRetries) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Resilience:Retry:MaxRetries"] = maxRetries })
        .Build();

    public sealed class OtherOptions;

    private sealed class ObservingWorker : IHostedService
    {
        public bool Started { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken) { Started = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CountingStrategy : Strategy, IDisposable
    {
        public int Executions { get; private set; }
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
        {
            Executions++;
            return next.InvokeAsync(context);
        }
    }

    private sealed class CountingDecorator(Action onDecorate) : IShieldDecorator
    {
        public Shield Decorate(Shield shield, string? name) { onDecorate(); return shield; }
        public Shield<TResult> Decorate<TResult>(Shield<TResult> shield, string? name) { onDecorate(); return shield; }
    }
}

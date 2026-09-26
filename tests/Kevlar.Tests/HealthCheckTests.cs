using Microsoft.Extensions.Configuration;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class HealthCheckTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Reports_All_States_For_Registry_And_Monitors(bool ratio, bool explicitMonitor)
    {
        var clock = new FakeTimeProvider();
        var monitor = new CircuitBreakerMonitor();
        var shield = CreateBreaker(monitor, ratio).WithTimeProvider(clock);
        var services = new ServiceCollection().AddLogging();
        if (!explicitMonitor)
        {
            services.AddShield("dependency", shield);
        }

        services.AddHealthChecks().AddKevlar(configure: options =>
        {
            if (explicitMonitor) { options.Monitors.Add("dependency", monitor); }
        });
        await using var provider = services.BuildServiceProvider();
        await Assert.That((await Read(provider)).Status).IsEqualTo(HealthStatus.Healthy);

        Trip(shield);
        var open = await Read(provider);
        await Assert.That(open.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(States(open, explicitMonitor)[0]).IsEqualTo("Open");

        clock.Advance(TimeSpan.FromMinutes(1));
        var halfOpen = await Read(provider);
        await Assert.That(halfOpen.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(States(halfOpen, explicitMonitor)[0]).IsEqualTo("HalfOpen");

        monitor.Isolate();
        var isolated = await Read(provider);
        await Assert.That(isolated.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(States(isolated, explicitMonitor)[0]).IsEqualTo("Isolated");
        monitor.Reset();
        await Assert.That((await Read(provider)).Status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    public async Task Status_Overrides_Are_Applied_Per_Circuit()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = CreateBreaker(monitor);
        var services = new ServiceCollection().AddLogging().AddShield("dependency", shield);
        services.AddHealthChecks().AddKevlar(failureStatus: HealthStatus.Unhealthy,
            configure: options => options.IsolatedStatus = HealthStatus.Degraded);
        await using var provider = services.BuildServiceProvider();
        Trip(shield);
        await Assert.That((await Read(provider)).Status).IsEqualTo(HealthStatus.Unhealthy);
        monitor.Isolate();
        await Assert.That((await Read(provider)).Status).IsEqualTo(HealthStatus.Degraded);
    }

    [Test]
    public async Task Registry_And_Monitor_States_Are_Combined_And_Each_Breaker_Is_Listed()
    {
        var monitor = new CircuitBreakerMonitor();
        var healthy = CreateBreaker(monitor);
        var isolated = CreateBreaker(monitor);
        monitor.Isolate();
        var services = new ServiceCollection().AddLogging().AddShield("healthy", Shield.Retry(1));
        services.AddHealthChecks().AddKevlar(configure: options => options.Monitors.Add("external", monitor));
        await using var provider = services.BuildServiceProvider();
        var entry = await Read(provider);
        await Assert.That(entry.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(((string[])((Dictionary<string, object>)entry.Data["monitors"])["external"]).Length).IsEqualTo(2);
        await Assert.That(((object[])entry.Data["shields"]).Length).IsEqualTo(1);
        GC.KeepAlive(healthy);
        GC.KeepAlive(isolated);
    }

    [Test]
    public async Task Filter_Runs_Before_Factories_And_Applies_To_Partitions()
    {
        var services = new ServiceCollection().AddLogging()
            .AddShield("included", Shield.Empty)
            .AddShield("excluded", _ => throw new InvalidOperationException("Must not resolve"))
            .AddPartitionedShield<string>("excluded", (_, _) => throw new InvalidOperationException("Must not create"));
        services.AddHealthChecks().AddKevlar(configure: options => options.ShieldFilter = name => name == "included");
        await using var provider = services.BuildServiceProvider();
        var entry = await Read(provider);
        await Assert.That(entry.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(((object[])entry.Data["shields"]).Length).IsEqualTo(1);
        await Assert.That(((object[])entry.Data["partitions"]).Length).IsEqualTo(0);
    }

    [Test]
    public async Task Typed_And_Dynamic_Registry_Entries_Are_Inspected_And_Removals_Disappear()
    {
        var monitor = new CircuitBreakerMonitor();
        var services = new ServiceCollection().AddLogging().AddShield("same", Shield.Empty)
            .AddShield("typed", Shield<int>.Empty.CircuitBreaker(options => options.Monitor = monitor));
        services.AddHealthChecks().AddKevlar();
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();
        registry.GetShield<int>("typed");
        monitor.Isolate();
        var entry = await Read(provider);
        await Assert.That(entry.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(((object[])entry.Data["shields"]).Length).IsEqualTo(2);
        registry.Remove<int>("typed");
        var dynamicMonitor = new CircuitBreakerMonitor();
        registry.TryAdd("dynamic", _ => CreateBreaker(dynamicMonitor));
        await Assert.That((await Read(provider)).Status).IsEqualTo(HealthStatus.Healthy);
        dynamicMonitor.Isolate();
        await Assert.That((await Read(provider)).Status).IsEqualTo(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task Partitions_Count_Open_Partitions_Once_And_Do_Not_Create_New_Ones()
    {
        var created = 0;
        var monitors = new Dictionary<string, CircuitBreakerMonitor>();
        var services = new ServiceCollection().AddLogging().AddPartitionedShield<string>("tenants", (_, key) =>
        {
            created++;
            var monitor = new CircuitBreakerMonitor();
            monitors.Add(key, monitor);
            return CreateBreaker(monitor).CircuitBreaker(options =>
            {
                options.Monitor = monitor;
                options.ConsecutiveFailures = 1;
                options.BreakDuration = TimeSpan.FromMinutes(1);
            });
        });
        services.AddHealthChecks().AddKevlar();
        await using var provider = services.BuildServiceProvider();
        var partitions = provider.GetRequiredKeyedService<PartitionedShield<string>>("tenants");
        await Read(provider);
        await Assert.That(created).IsEqualTo(0);
        Trip(partitions.GetShield("open"));
        partitions.GetShield("isolated");
        monitors["isolated"].Isolate();
        partitions.GetShield("closed");
        var entry = await Read(provider);
        var data = (Dictionary<string, object>)((object[])entry.Data["partitions"])[0];
        await Assert.That(entry.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That((int)data["partitionCount"]).IsEqualTo(3);
        await Assert.That((int)data["openPartitionCount"]).IsEqualTo(1);
        await Assert.That((int)data["isolatedPartitionCount"]).IsEqualTo(1);
        await Assert.That(((string[][])data["breakers"]).Length).IsEqualTo(3);
        await Assert.That(((string[][])data["breakers"])[0].Length).IsEqualTo(2);
        await Assert.That(created).IsEqualTo(3);
        await partitions.ClearAsync();
        await Assert.That((await Read(provider)).Status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    public async Task Typed_Partitions_Use_Last_Registration_And_Preserve_Separate_Key_Types()
    {
        var monitor = new CircuitBreakerMonitor();
        var services = new ServiceCollection().AddLogging()
            .AddPartitionedShield<string, int>("same", (_, _) => throw new InvalidOperationException("Replaced"))
            .AddPartitionedShield<string, int>("same", (_, _) => Shield<int>.Empty.CircuitBreaker(options => options.Monitor = monitor))
            .AddPartitionedShield<int, int>("same", (_, _) => Shield<int>.Empty);
        services.AddHealthChecks().AddKevlar();
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<PartitionedShield<string, int>>("same").GetShield("key");
        monitor.Isolate();
        var entry = await Read(provider);
        await Assert.That(entry.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(((object[])entry.Data["partitions"]).Length).IsEqualTo(2);
    }

    [Test]
    public async Task Options_Are_Snapshotted_And_Registry_Can_Be_Disabled()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = CreateBreaker(monitor);
        KevlarHealthCheckOptions? captured = null;
        var services = new ServiceCollection().AddLogging().AddShield("unused", _ => throw new InvalidOperationException("Disabled"));
        services.AddHealthChecks().AddKevlar(configure: options =>
        {
            captured = options;
            options.IncludeRegistry = false;
            options.Monitors.Add("dependency", monitor);
        });
        captured!.IncludeRegistry = true;
        captured.Monitors.Clear();
        captured.IsolatedStatus = HealthStatus.Healthy;
        await using var provider = services.BuildServiceProvider();
        monitor.Isolate();
        var entry = await Read(provider);
        await Assert.That(entry.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(((object[])entry.Data["shields"]).Length).IsEqualTo(0);
        GC.KeepAlive(shield);
    }

    [Test]
    public async Task Unbound_Monitor_And_Failed_Factory_Report_Failure_Status()
    {
        var services = new ServiceCollection().AddLogging().AddShield("broken", _ => throw new InvalidOperationException("Factory failed"));
        services.AddHealthChecks().AddKevlar(name: "registry", failureStatus: HealthStatus.Unhealthy)
            .AddKevlar(name: "monitor", configure: options =>
            {
                options.IncludeRegistry = false;
                options.Monitors.Add("unbound", new CircuitBreakerMonitor());
            });
        await using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        await Assert.That(report.Entries["registry"].Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(report.Entries["registry"].Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(report.Entries["monitor"].Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(report.Entries["monitor"].Exception).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task Registration_Preserves_Tags_Timeout_And_Cancellation()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHealthChecks().AddKevlar(tags: ["ready"], timeout: TimeSpan.FromSeconds(3));
        await using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single();
        await Assert.That(registration.Tags.Contains("ready")).IsTrue();
        await Assert.That(registration.Timeout).IsEqualTo(TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(() => registration.Factory(provider).CheckHealthAsync(
            new HealthCheckContext { Registration = registration }, cancellation.Token)).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Rejects_Invalid_Status_And_Null_Monitor()
    {
        var builder = new ServiceCollection().AddLogging().AddHealthChecks();
        await Assert.That(() => builder.AddKevlar(failureStatus: (HealthStatus)99)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => builder.AddKevlar(configure: options => options.IsolatedStatus = (HealthStatus)99)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => builder.AddKevlar(configure: options => options.Monitors.Add("null", null!))).Throws<ArgumentException>();
    }

    [Test]
    public async Task Reload_Inspects_Only_Current_Publication()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CircuitBreaker:ConsecutiveFailures"] = "1",
            ["CircuitBreaker:BreakDuration"] = "00:01:00"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddReloadingShield("dynamic",
            new ReloadingShieldOptions { DebounceDelay = TimeSpan.Zero }, configuration);
        services.AddHealthChecks().AddKevlar();
        await using var provider = services.BuildServiceProvider();
        var snapshots = provider.GetRequiredKeyedService<IShieldProvider>("dynamic");
        var old = snapshots.Current;
        Trip(old);
        await Assert.That((await Read(provider)).Status).IsEqualTo(HealthStatus.Degraded);
        configuration["CircuitBreaker:ConsecutiveFailures"] = "2";
        configuration.Reload();
        await Assert.That(snapshots.Current).IsNotSameReferenceAs(old);
        var current = await Read(provider);
        await Assert.That(current.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(States(current, explicitMonitor: false).Length).IsEqualTo(1);
    }

    private static Shield CreateBreaker(CircuitBreakerMonitor monitor, bool ratio = false) =>
        Shield.CircuitBreaker(options =>
        {
            options.Monitor = monitor;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            if (ratio)
            {
                options.FailureRatio = 0.5;
                options.MinimumThroughput = 1;
            }
            else
            {
                options.ConsecutiveFailures = 1;
            }
        });

    private static void Trip(Shield shield)
    {
        try { shield.Execute(static _ => throw new InvalidOperationException("Dependency failed")); }
        catch (InvalidOperationException) { }
    }

    private static async Task<HealthReportEntry> Read(ServiceProvider provider) =>
        (await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync()).Entries["kevlar"];

    private static string[] States(HealthReportEntry entry, bool explicitMonitor) => explicitMonitor
        ? (string[])((Dictionary<string, object>)entry.Data["monitors"])["dependency"]
        : (string[])((Dictionary<string, object>)((object[])entry.Data["shields"])[0])["breakers"];
}

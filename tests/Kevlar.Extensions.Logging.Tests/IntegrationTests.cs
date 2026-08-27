using System.Diagnostics.Metrics;
using System.Net;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace Kevlar.Extensions.Logging.Tests;

public class IntegrationTests
{
    [Test]
    [NotInParallel]
    public async Task NonReplayable_Request_Reports_Suppressed_Attempts()
    {
        var logs = new FakeLoggerProvider();
        var telemetry = new List<(string EventName, string? Reason)>();
        var measurements = new List<string>();
        using var subscription = KevlarDiagnostics.Listen(new CallbackTelemetryListener(telemetryEvent =>
        {
            if (telemetryEvent.EventName == "attempts_suppressed")
            {
                telemetry.Add((telemetryEvent.EventName, telemetryEvent.SuppressionReason));
            }
        }));
        using var meter = CreateReplaySuppressionListener(measurements);
        var transport = new SequenceHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddHttpClient("upload")
            .ConfigurePrimaryHttpMessageHandler(() => transport)
            .AddShield(HttpShield.WhenTransient().Retry(1, Backoff.None));
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("upload");
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://example.test/upload")
        {
            Content = new StreamContent(new MemoryStream([1, 2, 3])),
        };

        using var response = await client.SendAsync(request);

        var suppression = logs.Collector.GetSnapshot()
            .Single(record => record.Id == new EventId(1009, "AttemptsSuppressed"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(transport.Attempts).IsEqualTo(1);
        await Assert.That(telemetry.Count).IsEqualTo(1);
        await Assert.That(telemetry[0]).IsEqualTo(
            ("attempts_suppressed", (string?)"non_replayable_content"));
        await Assert.That(measurements).IsEquivalentTo(["non_replayable_content"]);
        await Assert.That(suppression.Level).IsEqualTo(LogLevel.Information);
        await Assert.That(suppression.GetStructuredStateValue("SuppressionReason"))
            .IsEqualTo("non_replayable_content");
        await Assert.That(suppression.GetStructuredStateValue("RequestMethod")).IsEqualTo("PUT");
        await Assert.That(suppression.GetStructuredStateValue("RequestUri"))
            .IsEqualTo("https://example.test/upload");
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Applies_To_Named_And_Reloading_Shields()
    {
        var logs = new FakeLoggerProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retry:MaxRetries"] = "1",
                ["Retry:Backoff"] = "None",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddShield("fixed", Shield.Retry(1, Backoff.None));
        services.AddReloadingShield("reloading", configuration);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        _ = await registry.GetShield("fixed").ExecuteOutcomeAsync<int>(Fail);
        _ = await registry.GetShield("reloading").ExecuteOutcomeAsync<int>(Fail);
        configuration.Reload();
        _ = await registry.GetShield("reloading").ExecuteOutcomeAsync<int>(Fail);

        var retryLogs = logs.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1001, "Retry"))
            .ToArray();
        await Assert.That(retryLogs.Count(record =>
            record.GetStructuredStateValue("ShieldName") == "fixed")).IsEqualTo(1);
        await Assert.That(retryLogs.Count(record =>
            record.GetStructuredStateValue("ShieldName") == "reloading")).IsEqualTo(2);
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Applies_To_Options_Reloading_Shields()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddSingleton<IOptionsMonitor<ReloadOptions>>(
            new FixedOptionsMonitor<ReloadOptions>(new ReloadOptions()));
        services.AddReloadingShield<ReloadOptions>(
            "options-untyped",
            static (_, _) => Shield.Retry(1, Backoff.None));
        services.AddReloadingShield<ReloadOptions, int>(
            "options-typed",
            static (_, _) => Shield.For<int>().Retry(1, Backoff.None));
        using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredKeyedService<IShieldProvider>("options-untyped")
            .Current.ExecuteOutcomeAsync<int>(Fail);
        _ = await provider.GetRequiredKeyedService<IShieldProvider<int>>("options-typed")
            .Current.ExecuteOutcomeAsync(Fail);

        var retryLogs = logs.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1001, "Retry"))
            .ToArray();
        await Assert.That(retryLogs.Count(record =>
            record.GetStructuredStateValue("ShieldName") == "options-untyped")).IsEqualTo(1);
        await Assert.That(retryLogs.Count(record =>
            record.GetStructuredStateValue("ShieldName") == "options-typed")).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task Standard_Shield_Logs_Request_Without_Query_Secrets()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddHttpClient("payments")
            .ConfigurePrimaryHttpMessageHandler(() => new SequenceHandler(
                HttpStatusCode.InternalServerError,
                HttpStatusCode.OK))
            .AddStandardShield(options =>
            {
                options.TotalTimeout.Timeout = Timeout.InfiniteTimeSpan;
                options.AttemptTimeout.Timeout = Timeout.InfiniteTimeSpan;
                options.Retry.MaxRetries = 1;
                options.Retry.Backoff = Backoff.None;
                options.UseRetryAfterHeader = false;
            });
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("payments");

        using var response = await client.GetAsync(
            "https://user:password@example.test/orders/42?token=secret");

        var retry = logs.Collector.GetSnapshot()
            .Single(record => record.Id == new EventId(1001, "Retry"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(retry.GetStructuredStateValue("ShieldName")).IsEqualTo("payments");
        await Assert.That(retry.GetStructuredStateValue("RequestMethod")).IsEqualTo("GET");
        await Assert.That(retry.GetStructuredStateValue("RequestUri"))
            .IsEqualTo("https://example.test/orders/42");
        await Assert.That(retry.Message).DoesNotContain("secret");
        await Assert.That(retry.Message).DoesNotContain("password");
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Decorates_PerRequest_Selected_Shields()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddHttpClient("selected")
            .ConfigurePrimaryHttpMessageHandler(() => new SequenceHandler(
                HttpStatusCode.InternalServerError,
                HttpStatusCode.OK))
            .AddShield(static (_, _) => HttpShield.WhenTransient().Retry(1, Backoff.None));
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("selected");

        using var response = await client.GetAsync("https://example.test/selected");

        var retry = logs.Collector.GetSnapshot()
            .Single(record => record.Id == new EventId(1001, "Retry"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(retry.GetStructuredStateValue("ShieldName")).IsEqualTo("selected");
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Decorates_Partition_Selected_Shields()
    {
        var logs = new FakeLoggerProvider();
        var partitions = new PartitionedShield<string, HttpResponseMessage>(
            static _ => HttpShield.WhenTransient().Retry(1, Backoff.None));
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddHttpClient("partitioned")
            .ConfigurePrimaryHttpMessageHandler(() => new SequenceHandler(
                HttpStatusCode.InternalServerError,
                HttpStatusCode.OK))
            .AddShield(partitions, static _ => "tenant");
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("partitioned");

        using var response = await client.GetAsync("https://example.test/partitioned");

        var retry = logs.Collector.GetSnapshot()
            .Single(record => record.Id == new EventId(1001, "Retry"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(retry.GetStructuredStateValue("ShieldName")).IsEqualTo("partitioned");
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Does_Not_Decorate_Di_Shields_Twice()
    {
        var logs = new FakeLoggerProvider();
        var decorator = new CountingDecorator();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton<IShieldDecorator>(decorator);
        services.AddKevlarLogging();
        services.AddShield<HttpResponseMessage>(
            "downstream",
            HttpShield.WhenTransient().Retry(1, Backoff.None));
        services.AddHttpClient("factory")
            .ConfigurePrimaryHttpMessageHandler(() => new SequenceHandler(
                HttpStatusCode.InternalServerError,
                HttpStatusCode.OK))
            .AddShield(static serviceProvider => serviceProvider
                .GetRequiredService<IKevlarRegistry>()
                .GetShield<HttpResponseMessage>("downstream"));
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("factory");

        using var response = await client.GetAsync("https://example.test/factory");

        var retries = logs.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1001, "Retry"))
            .ToArray();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(retries.Length).IsEqualTo(1);
        await Assert.That(retries[0].GetStructuredStateValue("ShieldName"))
            .IsEqualTo("downstream");
        await Assert.That(decorator.Count).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Decorates_Direct_Request_Shield()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddHttpClient("direct")
            .ConfigurePrimaryHttpMessageHandler(() => new SequenceHandler(
                HttpStatusCode.InternalServerError,
                HttpStatusCode.OK))
            .AddShield(Shield<HttpResponseMessage>.Empty);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("direct");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/direct")
            .WithShield(HttpShield.WhenTransient().Retry(1, Backoff.None));

        using var response = await client.SendAsync(request);

        var retry = logs.Collector.GetSnapshot()
            .Single(record => record.Id == new EventId(1001, "Retry"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(retry.GetStructuredStateValue("ShieldName")).IsEqualTo("direct");
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Decorates_Standard_Hedge_Endpoint_Shields()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddHttpClient("hedged")
            .ConfigurePrimaryHttpMessageHandler(() => new SequenceHandler(
                HttpStatusCode.InternalServerError))
            .AddStandardHedgeShield(options =>
            {
                options.TotalTimeout = TimeSpan.FromMinutes(1);
                options.MaxHedgedAttempts = 0;
                options.AttemptTimeout = TimeSpan.FromMinutes(1);
                options.ConsecutiveFailures = 1;
                options.FailureRatio = null;
                options.Endpoints.Add(new HttpEndpoint(new Uri("https://endpoint.test")));
            });
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("hedged");

        using var response = await client.GetAsync("https://origin.test/resource");

        var transition = logs.Collector.GetSnapshot()
            .Single(record => record.Id == new EventId(1003, "CircuitState"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(transition.GetStructuredStateValue("ShieldName")).IsEqualTo("hedged");
        await Assert.That(transition.GetStructuredStateValue("ToState")).IsEqualTo("Open");
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Does_Not_Redecorate_Outer_Transparent_Shields()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddShield<HttpResponseMessage>("empty", Shield<HttpResponseMessage>.Empty);
        services.AddHttpClient("composed")
            .ConfigurePrimaryHttpMessageHandler(() => new SequenceHandler(
                HttpStatusCode.InternalServerError,
                HttpStatusCode.OK))
            .AddShield(static (_, serviceProvider) => serviceProvider
                .GetRequiredService<IKevlarRegistry>()
                .GetShield<HttpResponseMessage>("empty")
                .Wrap(HttpShield.WhenTransient().Retry(1, Backoff.None)));
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("composed");

        using var response = await client.GetAsync("https://example.test/composed");

        var retries = logs.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1001, "Retry"))
            .ToArray();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(retries.Length).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Decorates_Runtime_Registry_Shields()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddKevlar();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        var untyped = registry.GetOrAdd(
            "dynamic-untyped",
            static _ => Shield.Retry(1, Backoff.None));
        var added = registry.TryAdd<int>(
            "dynamic-typed",
            static _ => Shield.For<int>().Retry(1, Backoff.None));
        var typed = registry.GetShield<int>("dynamic-typed");

        _ = await untyped.ExecuteOutcomeAsync<int>(Fail);
        _ = await typed.ExecuteOutcomeAsync(Fail);

        var shieldNames = logs.Collector.GetSnapshot()
            .Where(record => record.Id == new EventId(1001, "Retry"))
            .Select(record => record.GetStructuredStateValue("ShieldName")!)
            .ToArray();
        await Assert.That(added).IsTrue();
        await Assert.That(shieldNames).IsEquivalentTo(["dynamic-untyped", "dynamic-typed"]);
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Does_Not_Redecorate_Repeated_Composition()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging();
        services.AddShield("leading", Shield.Empty);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();
        var repeated = registry.GetShield("leading")
            .Wrap(Shield.Retry(1, Backoff.None))
            .Wrap(Shield.Timeout(TimeSpan.FromSeconds(1)));
        var dynamicShield = registry.GetOrAdd("repeated", _ => repeated);

        _ = await dynamicShield.ExecuteOutcomeAsync<int>(Fail);

        var retries = logs.Collector.GetSnapshot()
            .Count(record => record.Id == new EventId(1001, "Retry"));
        await Assert.That(retries).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task AddKevlarLogging_Shares_Rate_Limit_Across_Decorated_Shields()
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddKevlarLogging(options => options.MaxLogsPerSecond = 1);
        services.AddShield("first", Shield.Retry(1, Backoff.None));
        services.AddShield("second", Shield.Retry(1, Backoff.None));
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        _ = await registry.GetShield("first").ExecuteOutcomeAsync<int>(Fail);
        _ = await registry.GetShield("second").ExecuteOutcomeAsync<int>(Fail);

        var retryLogs = logs.Collector.GetSnapshot()
            .Count(record => record.Id == new EventId(1001, "Retry"));
        await Assert.That(retryLogs).IsEqualTo(1);
    }

    private static ValueTask<int> Fail(CancellationToken _) =>
        new(Task.FromException<int>(new TestException("failure")));

    private static MeterListener CreateReplaySuppressionListener(List<string> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, activeListener) =>
            {
                if (instrument.Meter.Name == KevlarDiagnostics.MeterName
                    && instrument.Name == "kevlar.http.replay_suppressed")
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (measurement == 1 && tag.Key == "kevlar.suppression.reason")
                {
                    measurements.Add(tag.Value?.ToString() ?? string.Empty);
                }
            }
        });
        listener.Start();
        return listener;
    }

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _index;

        public int Attempts => Volatile.Read(ref _index);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return Task.FromResult(new HttpResponseMessage(statuses[index]));
        }
    }

    private sealed class CallbackTelemetryListener(Action<KevlarTelemetryEvent> callback)
        : IKevlarTelemetryListener
    {
        public void OnEvent(in KevlarTelemetryEvent telemetryEvent) => callback(telemetryEvent);
    }

    private sealed class TestException(string message) : Exception(message);

    private sealed class CountingDecorator : IShieldDecorator
    {
        public int Count { get; private set; }

        public Shield Decorate(Shield shield, string? name)
        {
            Count++;
            return shield;
        }

        public Shield<TResult> Decorate<TResult>(Shield<TResult> shield, string? name)
        {
            Count++;
            return shield;
        }
    }

    private sealed class ReloadOptions
    {
    }

    private sealed class FixedOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => value;

        public TOptions Get(string? name) => value;

        public IDisposable OnChange(Action<TOptions, string?> listener) => NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

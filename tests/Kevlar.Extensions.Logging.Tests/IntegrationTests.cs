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

        using var response = await client.GetAsync("https://example.test/orders/42?token=secret");

        var retry = logs.Collector.GetSnapshot()
            .Single(record => record.Id == new EventId(1001, "Retry"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(retry.GetStructuredStateValue("ShieldName")).IsEqualTo("payments");
        await Assert.That(retry.GetStructuredStateValue("RequestMethod")).IsEqualTo("GET");
        await Assert.That(retry.GetStructuredStateValue("RequestUri"))
            .IsEqualTo("https://example.test/orders/42");
        await Assert.That(retry.Message).DoesNotContain("secret");
    }

    private static ValueTask<int> Fail(CancellationToken _) =>
        new(Task.FromException<int>(new TestException("failure")));

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return Task.FromResult(new HttpResponseMessage(statuses[index]));
        }
    }

    private sealed class TestException(string message) : Exception(message);

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

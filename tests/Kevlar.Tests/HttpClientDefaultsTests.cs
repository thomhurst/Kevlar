using System.Collections.Concurrent;
using System.Net;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class HttpClientDefaultsTests
{
    [Test]
    [Arguments(Registration.Standard)]
    [Arguments(Registration.StandardServiceProvider)]
    [Arguments(Registration.StandardConfiguration)]
    [Arguments(Registration.ShieldFactory)]
    [Arguments(Registration.ShieldAndOptionsFactories)]
    public async Task Defaults_Isolate_Circuits_For_Named_Default_And_Typed_Clients(Registration registration)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(builder =>
        {
            builder.ConfigurePrimaryHttpMessageHandler(CreateSuccessHandler);
            Register(builder, registration);
        });
        services.AddHttpClient("failing").ConfigurePrimaryHttpMessageHandler(() =>
            new FuncHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        services.AddHttpClient("healthy").ConfigurePrimaryHttpMessageHandler(CreateSuccessHandler);
        services.AddHttpClient<TypedClient>().ConfigurePrimaryHttpMessageHandler(CreateSuccessHandler);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var failing = factory.CreateClient("failing");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await failing.GetAsync("https://dependency.test/");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
        await Assert.That(async () => await failing.GetAsync("https://dependency.test/"))
            .Throws<CircuitOpenException>();

        using var healthy = factory.CreateClient("healthy");
        using var unnamed = factory.CreateClient();
        using var typed = provider.GetRequiredService<TypedClient>().Client;
        foreach (var client in new[] { healthy, unnamed, typed })
        {
            using var response = await client.GetAsync("https://dependency.test/");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    [Test]
    [Arguments(Registration.Standard)]
    [Arguments(Registration.StandardServiceProvider)]
    [Arguments(Registration.StandardConfiguration)]
    [Arguments(Registration.ShieldFactory)]
    [Arguments(Registration.ShieldAndOptionsFactories)]
    [Arguments(Registration.Hedge)]
    [Arguments(Registration.HedgeConfiguration)]
    public async Task Defaults_Isolate_Concurrency_Limits(Registration registration)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(builder => Register(builder, registration));
        services.AddHttpClient("busy").ConfigurePrimaryHttpMessageHandler(() => new FuncHandler(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        services.AddHttpClient("healthy").ConfigurePrimaryHttpMessageHandler(CreateSuccessHandler);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var busy = factory.CreateClient("busy");
        using var healthy = factory.CreateClient("healthy");
        var inFlight = busy.GetAsync("https://dependency.test/");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(async () => await busy.GetAsync("https://dependency.test/"))
                .Throws<ConcurrencyLimitExceededException>();
            using var response = await healthy.GetAsync("https://dependency.test/");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally
        {
            release.TrySetResult();
            using var response = await inFlight;
        }
    }

    [Test]
    public async Task Defaults_Reuse_One_Factory_Pipeline_Per_Client_Under_Concurrent_Creation()
    {
        var calls = 0;
        var decorator = new RecordingDecorator();
        var services = new ServiceCollection();
        services.AddSingleton<IShieldDecorator>(decorator);
        services.ConfigureHttpClientDefaults(builder => builder
            .ConfigurePrimaryHttpMessageHandler(CreateSuccessHandler)
            .AddShield(_ =>
            {
                Interlocked.Increment(ref calls);
                return Shield<HttpResponseMessage>.Empty;
            }));
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            using var client = factory.CreateClient(index % 2 == 0 ? "first" : "second");
            using var response = await client.GetAsync("https://dependency.test/");
        })));

        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(decorator.Names).IsEquivalentTo(new string?[] { "first", "second" });
    }

    [Test]
    public async Task Defaults_Reload_Each_Client_And_Unsubscribe_On_Provider_Disposal()
    {
        var configuration = CreateConfiguration();
        var calls = 0;
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(builder => builder
            .ConfigurePrimaryHttpMessageHandler(CreateSuccessHandler)
            .AddStandardShield(configuration, (_, _) => Interlocked.Increment(ref calls)));
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var first = factory.CreateClient("first");
        using var second = factory.CreateClient("second");

        await Assert.That(calls).IsEqualTo(2);
        configuration.Reload();
        await Assert.That(calls).IsEqualTo(4);
        using var firstResponse = await first.GetAsync("https://dependency.test/");
        using var secondResponse = await second.GetAsync("https://dependency.test/");
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        provider.Dispose();
        configuration.Reload();
        await Assert.That(calls).IsEqualTo(4);
    }

    [Test]
    public async Task Defaults_Keep_Multiple_Shield_Registrations_Independent()
    {
        var calls = 0;
        Shield<HttpResponseMessage> Create(IServiceProvider _)
        {
            Interlocked.Increment(ref calls);
            return Shield<HttpResponseMessage>.Empty;
        }

        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(builder => builder
            .ConfigurePrimaryHttpMessageHandler(CreateSuccessHandler)
            .AddShield(Create)
            .AddShield(Create));
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var first = factory.CreateClient("first");
        using var second = factory.CreateClient("second");
        using var firstAgain = factory.CreateClient("first");

        await Assert.That(calls).IsEqualTo(4);
    }

    [Test]
    public async Task Defaults_Snapshot_Standard_Options_Without_Repeating_Configuration()
    {
        StandardHttpShieldOptions? captured = null;
        var calls = 0;
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(builder => builder
            .ConfigurePrimaryHttpMessageHandler(CreateSuccessHandler)
            .AddStandardShield(options =>
            {
                calls++;
                ConfigureStandard(options);
                captured = options;
            }));
        captured!.AttemptTimeout.Timeout = TimeSpan.Zero;
        captured.Retry.MaxRetries = -1;
        captured.CircuitBreaker.MinimumThroughput = -1;
        captured.ConcurrencyLimit!.MaxConcurrency = 0;
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        foreach (var name in new[] { "first", "second" })
        {
            using var client = factory.CreateClient(name);
            using var response = await client.GetAsync("https://dependency.test/");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await Assert.That(calls).IsEqualTo(1);
    }

    private static void Register(IHttpClientBuilder builder, Registration registration)
    {
        switch (registration)
        {
            case Registration.Standard:
                builder.AddStandardShield(ConfigureStandard);
                break;
            case Registration.StandardServiceProvider:
                builder.AddStandardShield((_, options) => ConfigureStandard(options));
                break;
            case Registration.StandardConfiguration:
                builder.AddStandardShield(CreateConfiguration());
                break;
            case Registration.ShieldFactory:
                builder.AddShield(_ => CreateShield());
                break;
            case Registration.ShieldAndOptionsFactories:
                builder.AddShield(_ => CreateShield(), _ => new ShieldHttpHandlerOptions());
                break;
            case Registration.Hedge:
                builder.AddStandardHedgeShield(options =>
                {
                    options.Hedge.Delay = TimeSpan.FromMinutes(1);
                    options.ConcurrencyLimit = new ConcurrencyLimitOptions { MaxConcurrency = 1, QueueLimit = 0 };
                });
                break;
            case Registration.HedgeConfiguration:
                builder.AddStandardHedgeShield(CreateConfiguration());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(registration));
        }
    }

    private static Shield<HttpResponseMessage> CreateShield()
    {
        var options = new StandardHttpShieldOptions();
        ConfigureStandard(options);
        return HttpShield.Standard(options);
    }

    private static void ConfigureStandard(StandardHttpShieldOptions options)
    {
        options.Retry.MaxRetries = 0;
        options.CircuitBreaker.MinimumThroughput = 2;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromDays(1);
        options.ConcurrencyLimit = new ConcurrencyLimitOptions { MaxConcurrency = 1, QueueLimit = 0 };
    }

    private static IConfigurationRoot CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Retry:MaxRetries"] = "0",
            ["CircuitBreaker:MinimumThroughput"] = "2",
            ["CircuitBreaker:BreakDuration"] = "1.00:00:00",
            ["ConcurrencyLimit:MaxConcurrency"] = "1",
            ["ConcurrencyLimit:QueueLimit"] = "0",
            ["Hedge:Delay"] = "00:01:00",
        }).Build();

    private static HttpMessageHandler CreateSuccessHandler() => new FuncHandler((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

    public enum Registration
    {
        Standard,
        StandardServiceProvider,
        StandardConfiguration,
        ShieldFactory,
        ShieldAndOptionsFactories,
        Hedge,
        HedgeConfiguration,
    }

    public sealed class TypedClient(HttpClient client)
    {
        public HttpClient Client { get; } = client;
    }

    private sealed class RecordingDecorator : IShieldDecorator
    {
        public ConcurrentBag<string?> Names { get; } = [];

        public Shield Decorate(Shield shield, string? name) => shield;

        public Shield<TResult> Decorate<TResult>(Shield<TResult> shield, string? name)
        {
            Names.Add(name);
            return shield;
        }
    }

    private sealed class FuncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }
}

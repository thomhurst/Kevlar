using System.Collections.Concurrent;
using System.Net;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.Tests;

public class HttpConfigurationReloadTests
{
    [Test]
    public async Task Registration_Null_Guards_Report_Exact_Parameters()
    {
        var configuration = BuildConfiguration(("Endpoints:0:Uri", "https://one.example"));
        IHttpClientBuilder? missingBuilder = null;
        var builder = new ServiceCollection().AddHttpClient("client");

        var builderError = await Assert.That(() => missingBuilder!.AddStandardShield(configuration))
            .Throws<ArgumentNullException>();
        var configurationError = await Assert.That(() => builder.AddStandardShield((IConfiguration)null!))
            .Throws<ArgumentNullException>();
        var configureError = await Assert.That(() => builder.AddStandardShield(
                configuration,
                (Action<IServiceProvider, StandardHttpShieldOptions>)null!))
            .Throws<ArgumentNullException>();
        var hedgeConfigurationError = await Assert.That(() => builder.AddStandardHedgeShield((IConfiguration)null!))
            .Throws<ArgumentNullException>();

        await Assert.That(builderError!.ParamName).IsEqualTo("builder");
        await Assert.That(configurationError!.ParamName).IsEqualTo("configuration");
        await Assert.That(configureError!.ParamName).IsEqualTo("configure");
        await Assert.That(hedgeConfigurationError!.ParamName).IsEqualTo("configuration");
    }

    [Test]
    public async Task Standard_Section_Binds_And_Service_Configuration_Wins()
    {
        var root = BuildConfiguration(
            ("Clients:GitHub:Retry:MaxRetries", "0"),
            ("Clients:GitHub:Retry:Backoff", "None"));
        var transport = new FuncHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var services = new ServiceCollection()
            .AddSingleton(new RetryOverride(2))
            .AddHttpClient("github")
            .ConfigurePrimaryHttpMessageHandler(() => transport)
            .AddStandardShield(
                root.GetSection("Clients:GitHub"),
                static (provider, options) =>
                {
                    options.Retry.MaxRetries = provider.GetRequiredService<RetryOverride>().MaxRetries;
                    options.Retry.Backoff = Backoff.None;
                })
            .Services
            .BuildServiceProvider();
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("github");

        using var response = await client.GetAsync("https://example.test/");

        await Assert.That(transport.Calls).IsEqualTo(3);
    }

    [Test]
    public async Task Standard_Section_Binds_All_Supported_Values()
    {
        var configuration = BuildConfiguration(
            ("TotalTimeout:Timeout", "00:00:20"),
            ("Retry:MaxRetries", "4"),
            ("Retry:Backoff", "Linear"),
            ("Retry:BaseDelay", "00:00:00.100"),
            ("Retry:BackoffMaxDelay", "00:00:02"),
            ("Retry:MaxDelay", "00:00:01"),
            ("CircuitBreaker:ConsecutiveFailures", "2"),
            ("CircuitBreaker:FailureRatio", ""),
            ("CircuitBreaker:MinimumThroughput", "5"),
            ("CircuitBreaker:SamplingWindow", "00:00:08"),
            ("CircuitBreaker:BreakDuration", "00:00:04"),
            ("ConcurrencyLimit:MaxConcurrency", "3"),
            ("ConcurrencyLimit:QueueLimit", "4"),
            ("AttemptTimeout", "00:00:02"),
            ("Handler:ContentReplayPolicy", "Buffer"),
            ("Handler:MaximumBufferSize", "4096"),
            ("Handler:AllowUnsafeMethodReplay", "true"),
            ("Handler:Routing:SelectionMode", "Weighted"),
            ("Handler:Routing:Seed", "7"),
            ("Handler:Routing:Endpoints:0:Uri", "https://one.example"),
            ("Handler:Routing:Endpoints:0:Weight", "2"));
        StandardHttpShieldOptions? bound = null;
        var services = new ServiceCollection();
        services.AddHttpClient("client")
            .ConfigurePrimaryHttpMessageHandler(() => new FuncHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
            .AddStandardShield(configuration, (_, options) => bound = options);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("client");
        using (await client.GetAsync("https://origin.example/"))
        {
        }

        await Assert.That(bound).IsNotNull();
        await Assert.That(bound!.TotalTimeout.Timeout).IsEqualTo(TimeSpan.FromSeconds(20));
        await Assert.That(bound.Retry.MaxRetries).IsEqualTo(4);
        await Assert.That(bound.Retry.Backoff.ToString()).IsEqualTo("linear 100ms steps, equal jitter, cap 2s");
        await Assert.That(bound.Retry.MaxDelay).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(bound.CircuitBreaker.ConsecutiveFailures).IsEqualTo(2);
        await Assert.That(bound.CircuitBreaker.FailureRatio).IsNull();
        await Assert.That(bound.CircuitBreaker.MinimumThroughput).IsEqualTo(5);
        await Assert.That(bound.CircuitBreaker.SamplingWindow).IsEqualTo(TimeSpan.FromSeconds(8));
        await Assert.That(bound.CircuitBreaker.BreakDuration).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(bound.CircuitBreaker)
            .IsTypeOf<CircuitBreakerOptions<HttpResponseMessage>>();
        await Assert.That(bound.ConcurrencyLimit!.MaxConcurrency).IsEqualTo(3);
        await Assert.That(bound.ConcurrencyLimit.QueueLimit).IsEqualTo(4);
        await Assert.That(bound.AttemptTimeout.Timeout).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(bound.Handler.ContentReplayPolicy).IsEqualTo(HttpContentReplayPolicy.Buffer);
        await Assert.That(bound.Handler.MaximumBufferSize).IsEqualTo(4096);
        await Assert.That(bound.Handler.AllowUnsafeMethodReplay).IsTrue();
        await Assert.That(bound.Handler.Routing!.SelectionMode).IsEqualTo(HttpEndpointSelectionMode.Weighted);
        await Assert.That(bound.Handler.Routing.Seed).IsEqualTo(7);
        await Assert.That(bound.Handler.Routing.Endpoints.Single().Uri.Host).IsEqualTo("one.example");
        await Assert.That(bound.Handler.Routing.Endpoints.Single().Weight).IsEqualTo(2);
    }

    [Test]
    [Arguments("None", "no delay")]
    [Arguments("Constant", "constant 100ms")]
    [Arguments("Linear", "linear 100ms steps, cap 2s")]
    [Arguments("Exponential", "exponential 100ms ×3, cap 2s")]
    public async Task Standard_Section_Binds_Each_Backoff(
        string kind,
        string expected)
    {
        var configuration = BuildConfiguration(
            ("Retry:Backoff", kind),
            ("Retry:BaseDelay", "00:00:00.100"),
            ("Retry:Factor", "3"),
            ("Retry:Jitter", "None"),
            ("Retry:BackoffMaxDelay", "00:00:02"));
        StandardHttpShieldOptions? bound = null;
        var services = new ServiceCollection();
        services.AddHttpClient("client")
            .AddStandardShield(configuration, (_, options) => bound = options);
        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IHttpClientFactory>().CreateClient("client");

        await Assert.That(bound!.Retry.Backoff.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("false", "exponential 100ms ×2, cap 30s")]
    [Arguments("true", "exponential 100ms ×2, equal jitter, cap 30s")]
    public async Task Standard_Section_Accepts_Legacy_Boolean_Jitter(
        string value,
        string expected)
    {
        var configuration = BuildConfiguration(
            ("Retry:BaseDelay", "00:00:00.100"),
            ("Retry:Jitter", value));
        StandardHttpShieldOptions? bound = null;
        var services = new ServiceCollection();
        services.AddHttpClient("client")
            .AddStandardShield(configuration, (_, options) => bound = options);
        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IHttpClientFactory>().CreateClient("client");

        await Assert.That(bound!.Retry.Backoff.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Hedge_Section_Binds_All_Supported_Values()
    {
        var configuration = BuildConfiguration(
            ("TotalTimeout", "00:00:20"),
            ("MaxAttempts", "3"),
            ("HedgeDelay", "00:00:00.250"),
            ("AttemptTimeout", "00:00:02"),
            ("MaxConcurrency", "4"),
            ("QueueLimit", "5"),
            ("ConsecutiveFailures", "2"),
            ("FailureRatio", ""),
            ("MinimumThroughput", "6"),
            ("SamplingWindow", "00:00:08"),
            ("BreakDuration", "00:00:04"),
            ("SelectionMode", "Weighted"),
            ("Seed", "9"),
            ("ContentReplayPolicy", "Buffer"),
            ("MaximumBufferSize", "8192"),
            ("AllowUnsafeMethodReplay", "true"),
            ("Endpoints:0", "https://one.example"));
        StandardHedgeShieldOptions? bound = null;
        var services = new ServiceCollection();
        services.AddHttpClient("client")
            .AddStandardHedgeShield(configuration, (_, options) => bound = options);
        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IHttpClientFactory>().CreateClient("client");

        await Assert.That(bound).IsNotNull();
        await Assert.That(bound!.TotalTimeout).IsEqualTo(TimeSpan.FromSeconds(20));
        await Assert.That(bound.MaxAttempts).IsEqualTo(3);
        await Assert.That(bound.HedgeDelay).IsEqualTo(TimeSpan.FromMilliseconds(250));
        await Assert.That(bound.AttemptTimeout).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(bound.MaxConcurrency).IsEqualTo(4);
        await Assert.That(bound.QueueLimit).IsEqualTo(5);
        await Assert.That(bound.ConsecutiveFailures).IsEqualTo(2);
        await Assert.That(bound.FailureRatio).IsNull();
        await Assert.That(bound.MinimumThroughput).IsEqualTo(6);
        await Assert.That(bound.SamplingWindow).IsEqualTo(TimeSpan.FromSeconds(8));
        await Assert.That(bound.BreakDuration).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(bound.SelectionMode).IsEqualTo(HttpEndpointSelectionMode.Weighted);
        await Assert.That(bound.Seed).IsEqualTo(9);
        await Assert.That(bound.ContentReplayPolicy).IsEqualTo(HttpContentReplayPolicy.Buffer);
        await Assert.That(bound.MaximumBufferSize).IsEqualTo(8192);
        await Assert.That(bound.AllowUnsafeMethodReplay).IsTrue();
        await Assert.That(bound.Endpoints.Single().Uri.Host).IsEqualTo("one.example");
    }

    [Test]
    [Arguments("Retry:MaxRetries", "many", "an integer")]
    [Arguments("Handler:MaximumBufferSize", "large", "an integer")]
    [Arguments("CircuitBreaker:FailureRatio", "often", "a number")]
    [Arguments("Handler:AllowUnsafeMethodReplay", "perhaps", "a Boolean")]
    [Arguments("TotalTimeout", "soon", "a TimeSpan")]
    [Arguments("Handler:ContentReplayPolicy", "Sometimes", "a HttpContentReplayPolicy")]
    [Arguments("Handler:Routing:Endpoints:0:Uri", "relative", "an absolute URI")]
    public async Task Invalid_Standard_Values_Report_Exact_Paths(
        string key,
        string value,
        string expected)
    {
        var values = key.EndsWith(":Uri", StringComparison.Ordinal)
            ? new[] { (key, value) }
            : [(key, value), ("Handler:Routing:Endpoints:0:Uri", "https://one.example")];
        var configuration = BuildConfiguration(values);
        var builder = new ServiceCollection().AddHttpClient("client");

        await Assert.That(() => builder.AddStandardShield(configuration))
            .Throws<InvalidOperationException>()
            .WithMessage($"Configuration value '{value}' for '{key}' is not {expected}.");
    }

    [Test]
    [Arguments("TotalTimeout", "00:00:00", "must be positive")]
    [Arguments("Retry:MaxRetries", "-1", "must not be negative")]
    [Arguments("Retry:MaxDelay", "-00:00:01", "must not be negative")]
    [Arguments("Retry:BaseDelay", "-00:00:01", "must not be negative")]
    [Arguments("Retry:Factor", "0.5", "must be finite and at least 1")]
    [Arguments("Retry:BackoffMaxDelay", "-00:00:01", "must not be negative")]
    [Arguments("CircuitBreaker:ConsecutiveFailures", "0", "must be positive")]
    [Arguments("CircuitBreaker:FailureRatio", "2", "must be between 0 (exclusive) and 1 (inclusive)")]
    [Arguments("CircuitBreaker:MinimumThroughput", "0", "must be at least 1")]
    [Arguments("CircuitBreaker:SamplingWindow", "00:00:00", "must be positive")]
    [Arguments("CircuitBreaker:BreakDuration", "00:00:00", "must be positive")]
    [Arguments("ConcurrencyLimit:MaxConcurrency", "0", "must be positive")]
    [Arguments("ConcurrencyLimit:QueueLimit", "-1", "must not be negative")]
    [Arguments("AttemptTimeout", "00:00:00", "must be positive")]
    [Arguments("Handler:MaximumBufferSize", "0", "must be positive")]
    [Arguments("Handler:Routing:Endpoints:0:Weight", "0", "must be positive")]
    public async Task Invalid_Standard_Ranges_Report_Exact_Paths(
        string key,
        string value,
        string requirement)
    {
        var values = key.Contains("Endpoints", StringComparison.Ordinal)
            ? new[]
            {
                ("Handler:Routing:Endpoints:0:Uri", "https://one.example"),
                (key, value),
            }
            : [(key, value)];
        var configuration = BuildConfiguration(values);
        var builder = new ServiceCollection().AddHttpClient("client");

        await Assert.That(() => builder.AddStandardShield(configuration))
            .Throws<InvalidOperationException>()
            .WithMessage($"Configuration value '{value}' for '{key}' {requirement}.");
    }

    [Test]
    public async Task Circuit_Breaker_Modes_Cannot_Be_Configured_Together()
    {
        var configuration = BuildConfiguration(
            ("CircuitBreaker:ConsecutiveFailures", "2"),
            ("CircuitBreaker:FailureRatio", "0.5"));
        var builder = new ServiceCollection().AddHttpClient("client");

        await Assert.That(() => builder.AddStandardShield(configuration))
            .Throws<InvalidOperationException>()
            .WithMessage(
                "Configuration value '0.5' for 'CircuitBreaker:FailureRatio' cannot be set with ConsecutiveFailures.");
    }

    [Test]
    public async Task Invalid_Binding_Reports_The_Full_Section_Path()
    {
        var root = BuildConfiguration(("Clients:GitHub:Retry:MaxRetries", "many"));
        var builder = new ServiceCollection().AddHttpClient("github");

        await Assert.That(() => builder.AddStandardShield(root.GetSection("Clients:GitHub")))
            .Throws<InvalidOperationException>()
            .WithMessage(
                "Configuration value 'many' for 'Clients:GitHub:Retry:MaxRetries' is not an integer.");
    }

    [Test]
    public async Task Legacy_MaxQueue_Keys_Are_Rejected_With_Their_Full_Path()
    {
        var builder = new ServiceCollection().AddHttpClient("legacy");
        var standard = BuildConfiguration(("Clients:GitHub:ConcurrencyLimit:MaxQueue", "5"))
            .GetSection("Clients:GitHub");
        var hedging = BuildConfiguration(("Clients:GitHub:MaxQueue", "5"))
            .GetSection("Clients:GitHub");

        await Assert.That(() => builder.AddStandardShield(standard))
            .Throws<InvalidOperationException>()
            .WithMessage(
                "Configuration key 'Clients:GitHub:ConcurrencyLimit:MaxQueue' is not supported; use 'QueueLimit'.");
        await Assert.That(() => builder.AddStandardHedgeShield(hedging))
            .Throws<InvalidOperationException>()
            .WithMessage(
                "Configuration key 'Clients:GitHub:MaxQueue' is not supported; use 'QueueLimit'.");
    }

    [Test]
    public async Task Valid_Reload_Replaces_The_Complete_Pipeline()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "0"),
            ("Retry:Backoff", "None"));
        var transport = new FuncHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var services = CreateStandardServices(configuration, transport);
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("client");

        using (await client.GetAsync("https://example.test/"))
        {
        }
        await Assert.That(transport.Calls).IsEqualTo(1);

        configuration["Retry:MaxRetries"] = "2";
        configuration.Reload();
        using (await client.GetAsync("https://example.test/"))
        {
        }

        await Assert.That(transport.Calls).IsEqualTo(4);
    }

    [Test]
    public async Task Invalid_Reload_Keeps_Last_Good_And_Reports_The_Path()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "1"),
            ("Retry:Backoff", "None"));
        var transport = new FuncHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Exception? reported = null;
        using var services = CreateStandardServices(configuration, transport, error => reported = error);
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("client");

        configuration["Retry:MaxRetries"] = "invalid";
        configuration.Reload();
        using (await client.GetAsync("https://example.test/"))
        {
        }

        await Assert.That(transport.Calls).IsEqualTo(2);
        await Assert.That(reported).IsTypeOf<InvalidOperationException>();
        await Assert.That(reported!.Message)
            .IsEqualTo("Configuration value 'invalid' for 'Retry:MaxRetries' is not an integer.");
    }

    [Test]
    public async Task Reload_Reporting_Failure_Does_Not_Stop_Later_Changes()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "0"),
            ("Retry:Backoff", "None"));
        var transport = new FuncHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var services = CreateStandardServices(
            configuration,
            transport,
            _ => throw new InvalidOperationException("reporting"));
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("client");

        configuration["Retry:MaxRetries"] = "invalid";
        configuration.Reload();
        configuration["Retry:MaxRetries"] = "2";
        configuration.Reload();
        using (await client.GetAsync("https://example.test/"))
        {
        }

        await Assert.That(transport.Calls).IsEqualTo(3);
    }

    [Test]
    public async Task In_Flight_Request_Retains_The_Snapshot_It_Started_With()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "1"),
            ("Retry:Backoff", "None"));
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FuncHandler(async (_, _) =>
        {
            if (firstAttempt.TrySetResult())
            {
                await continueAttempt.Task;
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var services = CreateStandardServices(configuration, transport);
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("client");

        var send = client.GetAsync("https://example.test/");
        await firstAttempt.Task;
        configuration["Retry:MaxRetries"] = "3";
        configuration.Reload();
        continueAttempt.SetResult();
        using (await send)
        {
        }

        await Assert.That(transport.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task Concurrent_In_Flight_Requests_Retain_Their_Shared_Snapshot()
    {
        const int requestCount = 16;
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "0"),
            ("Retry:Backoff", "None"));
        using var entered = new CountdownEvent(requestCount);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FuncHandler(async (_, _) =>
        {
            entered.Signal();
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var services = CreateStandardServices(configuration, transport);
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("client");
        var sends = Enumerable.Range(0, requestCount)
            .Select(_ => client.GetAsync("https://example.test/"))
            .ToArray();

        await Task.Run(entered.Wait);
        configuration["Retry:MaxRetries"] = "2";
        configuration.Reload();
        release.SetResult();
        var responses = await Task.WhenAll(sends);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        await Assert.That(transport.Calls).IsEqualTo(requestCount);
    }

    [Test]
    public async Task Handler_Rotation_Creates_Fresh_State_And_Service_Configuration()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "0"));
        var configurations = 0;
        var services = new ServiceCollection();
        services.AddHttpClient("client")
            .SetHandlerLifetime(TimeSpan.FromSeconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => new FuncHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
            .AddStandardShield(
                configuration,
                (_, _) => Interlocked.Increment(ref configurations));
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using (var first = factory.CreateClient("client"))
        using (await first.GetAsync("https://example.test/"))
        {
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Volatile.Read(ref configurations) < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            using var rotated = factory.CreateClient("client");
            using var response = await rotated.GetAsync("https://example.test/");
        }

        await Assert.That(Volatile.Read(ref configurations)).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task Initial_Service_Configuration_Failure_Is_Propagated()
    {
        var configuration = BuildConfiguration(("Retry:MaxRetries", "0"));
        var services = new ServiceCollection();
        services.AddHttpClient("client")
            .AddStandardShield(
                configuration,
                static (_, _) => throw new InvalidOperationException("configure"));
        using var provider = services.BuildServiceProvider();

        await Assert.That(() => provider.GetRequiredService<IHttpClientFactory>().CreateClient("client"))
            .Throws<InvalidOperationException>()
            .WithMessage("configure");
    }

    [Test]
    public async Task Reload_Provider_Disposal_Is_Idempotent_And_Unsubscribes()
    {
        var configuration = BuildConfiguration();
        var creations = 0;
        var reloading = new ReloadingHttpShieldPipeline(
            () =>
            {
                Interlocked.Increment(ref creations);
                return new HttpShieldPipeline(
                    HttpShield.WhenTransient().Retry(0, Backoff.None),
                    new ShieldHttpHandlerOptions());
            },
            configuration.GetReloadToken,
            onReloadFailure: null);

        reloading.Dispose();
        reloading.Dispose();
        configuration.Reload();

        await Assert.That(creations).IsEqualTo(1);
    }

    [Test]
    public async Task Endpoint_Shield_Factory_Failure_Is_Not_Cached()
    {
        var creations = 0;
        var routing = new HttpEndpointRoutingOptions
        {
            ShieldFactory = _ =>
            {
                Interlocked.Increment(ref creations);
                return null!;
            },
        };
        routing.Endpoints.Add(new HttpEndpoint(new Uri("https://endpoint.example")));
        using var handler = new ShieldDelegatingHandler(
            HttpShield.WhenTransient().Retry(0, Backoff.None),
            new ShieldHttpHandlerOptions { Routing = routing })
        {
            InnerHandler = new FuncHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
        };
        using var invoker = new HttpMessageInvoker(handler);

        await Assert.That(async () => await invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://origin.example/"),
                CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://origin.example/"),
                CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(creations).IsEqualTo(2);
    }

    [Test]
    public async Task Handler_Options_Validate_Before_Send()
    {
        var invalidContent = new ShieldHttpHandlerOptions
        {
            ContentReplayPolicy = (HttpContentReplayPolicy)int.MaxValue,
        };
        var invalidBuffer = new ShieldHttpHandlerOptions { MaximumBufferSize = 0 };
        var invalidSelection = CreateRoutingOptions();
        invalidSelection.Routing!.SelectionMode = (HttpEndpointSelectionMode)int.MaxValue;
        var emptyRouting = new ShieldHttpHandlerOptions { Routing = new HttpEndpointRoutingOptions() };
        var nullEndpoint = new ShieldHttpHandlerOptions { Routing = new HttpEndpointRoutingOptions() };
        nullEndpoint.Routing.Endpoints.Add(null!);
        var shield = HttpShield.WhenTransient().Retry(0, Backoff.None);

        await Assert.That(() => new ShieldDelegatingHandler(shield, invalidContent))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ShieldDelegatingHandler(shield, invalidBuffer))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ShieldDelegatingHandler(shield, invalidSelection))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ShieldDelegatingHandler(shield, emptyRouting))
            .Throws<ArgumentException>();
        await Assert.That(() => new ShieldDelegatingHandler(shield, nullEndpoint))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Successful_Reload_Replaces_Circuit_State()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "0"),
            ("CircuitBreaker:ConsecutiveFailures", "1"),
            ("CircuitBreaker:FailureRatio", ""));
        var transport = new FuncHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var services = CreateStandardServices(configuration, transport);
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("client");

        using (await client.GetAsync("https://example.test/"))
        {
        }
        await Assert.That(async () => await client.GetAsync("https://example.test/"))
            .Throws<CircuitOpenException>();
        configuration.Reload();
        using (await client.GetAsync("https://example.test/"))
        {
        }

        await Assert.That(transport.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task Hedge_Section_Binds_Endpoints_And_Routes_Attempts()
    {
        var configuration = BuildConfiguration(
            ("MaxAttempts", "2"),
            ("HedgeDelay", "00:00:00"),
            ("Endpoints:0:Uri", "https://one.example"),
            ("Endpoints:1:Uri", "https://two.example"));
        var hosts = new ConcurrentBag<string>();
        var transport = new FuncHandler((request, _) =>
        {
            hosts.Add(request.RequestUri!.Host);
            var status = request.RequestUri.Host == "two.example"
                ? HttpStatusCode.OK
                : HttpStatusCode.InternalServerError;
            return Task.FromResult(new HttpResponseMessage(status));
        });
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient("hedged", client =>
                client.Timeout = TimeSpan.FromMilliseconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => transport)
            .AddStandardHedgeShield(configuration);
        using var services = serviceCollection.BuildServiceProvider();
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("hedged");

        using var response = await client.GetAsync("https://origin.example/path");

        await Assert.That(client.Timeout).IsEqualTo(Timeout.InfiniteTimeSpan);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(hosts).Contains("one.example");
        await Assert.That(hosts).Contains("two.example");
    }

    [Test]
    public async Task Configuration_Registration_Works_For_Typed_Named_Clients()
    {
        var configuration = BuildConfiguration(
            ("Retry:MaxRetries", "1"),
            ("Retry:Backoff", "None"));
        var transport = new FuncHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(
                Interlocked.Increment(ref _typedAttempts) == 1
                    ? HttpStatusCode.InternalServerError
                    : HttpStatusCode.OK)));
        _typedAttempts = 0;
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient<TypedClient>()
            .ConfigurePrimaryHttpMessageHandler(() => transport)
            .AddStandardShield(configuration);
        using var services = serviceCollection.BuildServiceProvider();

        using var response = await services.GetRequiredService<TypedClient>().GetAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(transport.Calls).IsEqualTo(2);
    }

    private static int _typedAttempts;

    private static ServiceProvider CreateStandardServices(
        IConfiguration configuration,
        HttpMessageHandler transport,
        Action<Exception>? onReloadFailure = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("client")
            .ConfigurePrimaryHttpMessageHandler(() => transport)
            .AddStandardShield(configuration, onReloadFailure);
        return services.BuildServiceProvider();
    }

    private static ShieldHttpHandlerOptions CreateRoutingOptions()
    {
        var routing = new HttpEndpointRoutingOptions();
        routing.Endpoints.Add(new HttpEndpoint(new Uri("https://endpoint.example")));
        return new ShieldHttpHandlerOptions { Routing = routing };
    }

    private static IConfigurationRoot BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private sealed record RetryOverride(int MaxRetries);

    public sealed class TypedClient(HttpClient client)
    {
        public Task<HttpResponseMessage> GetAsync() => client.GetAsync("https://example.test/");
    }

    private sealed class FuncHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return send(request, cancellationToken);
        }
    }
}

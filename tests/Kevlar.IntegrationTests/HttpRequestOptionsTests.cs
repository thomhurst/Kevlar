using System.Collections.Concurrent;
using System.Net;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.IntegrationTests;

public class HttpRequestOptionsTests
{
    private static readonly KevlarKey<string?> TenantKey = new("http.tenant");

    [Test]
    public async Task Typed_Request_Options_Key_Is_Used_Directly()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        var options = new KevlarRequestOptions { ShieldName = "direct" };

        request.Options.Set(KevlarHttp.RequestOptions, options);

        await Assert.That(ReferenceEquals(KevlarHttp.GetRequestOptions(request), options)).IsTrue();
    }

    [Test]
    public async Task Request_Properties_Are_Visible_And_Do_Not_Leak()
    {
        var observedByStrategy = new ConcurrentQueue<string?>();
        var observedByRetry = new ConcurrentQueue<string?>();
        var attempts = 0;
        var shield = HttpShield.WhenTransient()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    observedByRetry.Enqueue(retry.Context.Properties.GetOrDefault(TenantKey));
                    return default;
                };
            })
            .Use(new PropertyObserverStrategy(TenantKey, observedByStrategy));
        using var client = CreateClient(shield, (_, _) => Task.FromResult(
            new HttpResponseMessage(Interlocked.Increment(ref attempts) % 2 == 1
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK)));
        using var first = new HttpRequestMessage(HttpMethod.Get, "https://example.test/first")
            .WithKevlarProperties(properties => properties.Set(TenantKey, "north"));
        using var second = new HttpRequestMessage(HttpMethod.Get, "https://example.test/second");

        using var firstResponse = await client.SendAsync(first);
        using var secondResponse = await client.SendAsync(second);

        await Assert.That(observedByRetry).IsEquivalentTo(["north", null]);
        await Assert.That(observedByStrategy).IsEquivalentTo(["north", "north", null, null]);
    }

    [Test]
    public async Task Per_Request_Shield_Override_And_DisableReplay_Are_Isolated()
    {
        var attempts = 0;
        var defaultShield = HttpShield.WhenTransient().Retry(3, Backoff.None);
        var noRetry = HttpShield.WhenTransient().Retry(0, Backoff.None);
        using var client = CreateClient(defaultShield, (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        using var overridden = new HttpRequestMessage(HttpMethod.Get, "https://example.test/override")
            .WithShield(noRetry);
        using var disabled = new HttpRequestMessage(HttpMethod.Get, "https://example.test/disabled")
            .DisableReplay();
        using var normal = new HttpRequestMessage(HttpMethod.Get, "https://example.test/default");

        using (await client.SendAsync(overridden))
        {
        }

        var overrideAttempts = attempts;
        attempts = 0;
        using (await client.SendAsync(disabled))
        {
        }

        var disabledAttempts = attempts;
        attempts = 0;
        using (await client.SendAsync(normal))
        {
        }

        await Assert.That(overrideAttempts).IsEqualTo(1);
        await Assert.That(disabledAttempts).IsEqualTo(1);
        await Assert.That(attempts).IsEqualTo(4);
    }

    [Test]
    public async Task Per_Request_Replay_Opt_In_Allows_Unsafe_Method()
    {
        var attempts = 0;
        using var client = CreateClient(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            (_, _) => Task.FromResult(new HttpResponseMessage(
                Interlocked.Increment(ref attempts) == 1
                    ? HttpStatusCode.ServiceUnavailable
                    : HttpStatusCode.OK)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/orders")
        {
            Content = new StringContent("payload"),
        }.AllowReplay();

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task AllowReplay_Enables_Hedged_Unsafe_Method_With_Same_Headers()
    {
        var idempotencyKeys = new ConcurrentQueue<string>();
        using var client = CreateClient(
            HttpShield.WhenTransient().Hedge(1, TimeSpan.Zero),
            (request, _) =>
            {
                idempotencyKeys.Enqueue(request.Headers.TryGetValues("Idempotency-Key", out var values)
                    ? string.Join(",", values)
                    : "<missing>");
                return Task.FromResult(new HttpResponseMessage(idempotencyKeys.Count == 1
                    ? HttpStatusCode.ServiceUnavailable
                    : HttpStatusCode.OK));
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/orders")
        {
            Content = new StringContent("payload"),
        }.AllowReplay();
        request.Headers.Add("Idempotency-Key", "order-42");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(idempotencyKeys).IsEquivalentTo(["order-42", "order-42"]);
    }

    [Test]
    public async Task AllowReplay_Does_Not_Bypass_Content_Replay_Safety()
    {
        var attempts = 0;
        using var client = CreateClient(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/orders")
        {
            Content = new StreamContent(new MemoryStream([1, 2, 3])),
        }.AllowReplay();

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Last_Replay_Override_Wins()
    {
        var attempts = 0;
        using var client = CreateClient(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            });
        using var allowed = new HttpRequestMessage(HttpMethod.Get, "https://example.test/allowed")
            .DisableReplay()
            .AllowReplay();
        using var disabled = new HttpRequestMessage(HttpMethod.Get, "https://example.test/disabled")
            .AllowReplay()
            .DisableReplay();

        using (await client.SendAsync(allowed))
        {
        }

        var allowedAttempts = attempts;
        attempts = 0;
        using (await client.SendAsync(disabled))
        {
        }

        await Assert.That(allowedAttempts).IsEqualTo(2);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Selector_Overload_Picks_Shield_By_Route()
    {
        var attempts = new ConcurrentDictionary<string, int>();
        using var services = new ServiceCollection()
            .AddHttpClient("selected")
            .AddShield((request, _) => request.RequestUri!.AbsolutePath == "/a"
                ? HttpShield.WhenTransient().Retry(1, Backoff.None)
                : Shield<HttpResponseMessage>.Empty)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((request, _) =>
            {
                attempts.AddOrUpdate(request.RequestUri!.AbsolutePath, 1, static (_, count) => count + 1);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }))
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("selected");

        using var a = await client.GetAsync("https://example.test/a");
        using var b = await client.GetAsync("https://example.test/b");

        await Assert.That(attempts["/a"]).IsEqualTo(2);
        await Assert.That(attempts["/b"]).IsEqualTo(1);
    }

    [Test]
    public async Task Partition_Selector_Isolates_Circuit_Breakers()
    {
        var partitions = new PartitionedShield<string, HttpResponseMessage>(
            _ => HttpShield.WhenTransient().CircuitBreaker(
                consecutiveFailures: 1,
                breakDuration: TimeSpan.FromMinutes(1)));
        var attempts = new ConcurrentDictionary<string, int>();
        using var services = new ServiceCollection()
            .AddHttpClient("partitioned")
            .AddShield(partitions, request => request.Headers.GetValues("X-Tenant").Single())
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((request, _) =>
            {
                var tenant = request.Headers.GetValues("X-Tenant").Single();
                attempts.AddOrUpdate(tenant, 1, static (_, count) => count + 1);
                return Task.FromResult(new HttpResponseMessage(
                    tenant == "one" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
            }))
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("partitioned");

        using var first = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        first.Headers.Add("X-Tenant", "one");
        using var second = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        second.Headers.Add("X-Tenant", "two");
        using var firstAgain = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        firstAgain.Headers.Add("X-Tenant", "one");
        using var firstResponse = await client.SendAsync(first);
        using var secondResponse = await client.SendAsync(second);
        _ = await Assert.That(async () => await client.SendAsync(firstAgain))
            .Throws<CircuitOpenException>();

        await Assert.That(partitions.Count).IsEqualTo(2);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(attempts["one"]).IsEqualTo(1);
        await Assert.That(attempts["two"]).IsEqualTo(1);
    }

    [Test]
    public async Task Partition_Selector_Awaits_Asynchronous_Factory()
    {
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var partitions = PartitionedShield<string, HttpResponseMessage>.CreateAsync(async _ =>
        {
            factoryStarted.SetResult();
            await releaseFactory.Task;
            return Shield<HttpResponseMessage>.Empty;
        });
        using var services = new ServiceCollection()
            .AddHttpClient("async-partitioned")
            .AddShield(partitions, static _ => "tenant")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("async-partitioned");

        var send = client.GetAsync("https://example.test/");
        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(send.IsCompleted).IsFalse();
        releaseFactory.SetResult();
        using var response = await send.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Handler_Cancellation_Stops_Waiting_For_Asynchronous_Partition()
    {
        await AssertPartitionSelectionCancellation(requestScoped: false);
    }

    [Test]
    public async Task Request_Cancellation_Stops_Waiting_For_Asynchronous_Partition()
    {
        await AssertPartitionSelectionCancellation(requestScoped: true);
    }

    [Test]
    public async Task Request_Cancellation_Option_Links_With_Handler_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = CreateClient(Shield<HttpResponseMessage>.Empty, async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/cancel")
            .WithKevlarCancellationToken(cancellation.Token);

        var send = client.SendAsync(request);
        await started.Task;
        cancellation.Cancel();

        _ = await Assert.That(async () => await send).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Pre_Canceled_Request_Token_Skips_Shield_Selection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var selections = 0;
        var sends = 0;
        var handler = new ShieldDelegatingHandler(_ =>
        {
            Interlocked.Increment(ref selections);
            return Shield<HttpResponseMessage>.Empty;
        })
        {
            InnerHandler = new StubHandler((_, _) =>
            {
                Interlocked.Increment(ref sends);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }),
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/canceled")
            .WithKevlarCancellationToken(cancellation.Token);

        _ = await Assert.That(async () => await client.SendAsync(request))
            .Throws<OperationCanceledException>();

        await Assert.That(selections).IsEqualTo(0);
        await Assert.That(sends).IsEqualTo(0);
    }

    [Test]
    public async Task Replayed_Request_Carries_The_Same_Kevlar_Options()
    {
        var names = new ConcurrentQueue<string?>();
        var attempts = 0;
        using var client = CreateClient(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            (request, _) =>
            {
                names.Enqueue(KevlarHttp.GetRequestOptions(request).ShieldName);
                return Task.FromResult(new HttpResponseMessage(
                    Interlocked.Increment(ref attempts) == 1
                        ? HttpStatusCode.ServiceUnavailable
                        : HttpStatusCode.OK));
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/replay")
            .WithShieldName("tenant-shield");

        using var response = await client.SendAsync(request);

        var observedNames = names.ToArray();
        await Assert.That(observedNames).Count().IsEqualTo(2);
        await Assert.That(observedNames[0]).IsEqualTo("tenant-shield");
        await Assert.That(observedNames[1]).IsEqualTo("tenant-shield");
    }

    [Test]
    public async Task Hedged_Attempts_See_Request_Properties()
    {
        var observed = new ConcurrentQueue<string?>();
        var shield = Shield.For<HttpResponseMessage>()
            .Hedge(maxHedgedAttempts: 1, delay: TimeSpan.Zero)
            .Use(new PropertyObserverStrategy(TenantKey, observed));
        using var client = CreateClient(shield, static async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/hedge")
            .WithKevlarProperties(properties => properties.Set(TenantKey, "north"));

        using var response = await client.SendAsync(request);

        var observedProperties = observed.ToArray();
        await Assert.That(observedProperties).Count().IsEqualTo(2);
        await Assert.That(observedProperties.All(value => value == "north")).IsTrue();
    }

    private static HttpClient CreateClient(
        Shield<HttpResponseMessage> shield,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        var handler = new ShieldDelegatingHandler(shield)
        {
            InnerHandler = new StubHandler(send),
        };
        return new HttpClient(handler);
    }

    private static async Task AssertPartitionSelectionCancellation(bool requestScoped)
    {
        var factoryCalls = 0;
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var partitions = PartitionedShield<string, HttpResponseMessage>.CreateAsync(async _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.SetResult();
            await releaseFactory.Task;
            return Shield<HttpResponseMessage>.Empty;
        });
        using var services = new ServiceCollection()
            .AddHttpClient("cancel-partitioned")
            .AddShield(partitions, static _ => "tenant")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("cancel-partitioned");
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        if (requestScoped)
        {
            request.WithKevlarCancellationToken(cancellation.Token);
        }

        var send = client.SendAsync(
            request,
            requestScoped ? CancellationToken.None : cancellation.Token);
        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        _ = await Assert.That(async () => await send.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OperationCanceledException>();
        await Assert.That(releaseFactory.Task.IsCompleted).IsFalse();

        releaseFactory.SetResult();
        using var response = await client.GetAsync("https://example.test/")
            .WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(factoryCalls).IsEqualTo(1);
    }

    private sealed class PropertyObserverStrategy(
        KevlarKey<string?> key,
        ConcurrentQueue<string?> observed) : Strategy
    {
        protected override bool InvokesContinuationAtMostOnce => true;

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            observed.Enqueue(context.Properties.GetOrDefault(key));
            return next.InvokeAsync(context);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.IntegrationTests;

public class HttpReplayTests
{
    [Test]
    public async Task Bounded_Buffer_Replays_Content_And_Request_Metadata()
    {
        var observations = new List<(string Body, string Header, string ContentHeader, int Option)>();
        var discardedContent = new TrackingContent("discarded");
        var transport = new RecordingHandler(async (attempt, request, _) =>
        {
            observations.Add((
                await request.Content!.ReadAsStringAsync(),
                request.Headers.GetValues("x-request").Single(),
                request.Content.Headers.GetValues("x-content").Single(),
                request.Options.TryGetValue(new HttpRequestOptionsKey<int>("option"), out var value) ? value : -1));
            return attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = discardedContent }
                : new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions
            {
                ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
                MaximumBufferSize = 1024,
                AllowUnsafeMethodReplay = true,
            },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/api?q=1")
        {
            Content = new StringContent("payload"),
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        request.Headers.TryAddWithoutValidation("x-request", "request-value");
        request.Content.Headers.TryAddWithoutValidation("x-content", "content-value");
        request.Options.Set(new HttpRequestOptionsKey<int>("option"), 42);

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(observations.Count).IsEqualTo(2);
        await Assert.That(observations.All(static item => item == ("payload", "request-value", "content-value", 42))).IsTrue();
        await Assert.That(discardedContent.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Retry_Disposes_The_Superseded_Response_After_Final_Selection()
    {
        var supersededContent = new TrackingContent("superseded");
        var disposedBeforeRepeat = false;
        var transport = new RecordingHandler((attempt, _, _) =>
        {
            if (attempt == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = supersededContent,
                });
            }

            disposedBeforeRepeat = supersededContent.DisposeCount == 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions(),
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/api");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(disposedBeforeRepeat).IsTrue();
        await Assert.That(supersededContent.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Hedge_Defers_Prior_Response_Disposal_Until_Final_Selection()
    {
        var firstContent = new TrackingContent("first");
        var disposedBeforeHedge = false;
        var transport = new RecordingHandler((attempt, _, _) =>
        {
            if (attempt == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = firstContent,
                });
            }

            disposedBeforeHedge = firstContent.DisposeCount != 0;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("terminal"),
            });
        });
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Hedge(2, TimeSpan.Zero),
            new ShieldHttpHandlerOptions(),
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/api");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(disposedBeforeHedge).IsFalse();
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("terminal");
        await Assert.That(firstContent.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Put_With_StreamContent_NoBuffer_Returns_Original_Response()
    {
        var responseContent = new TrackingContent("original");
        var originalResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = responseContent,
        };
        var transport = new RecordingHandler(async (_, request, _) =>
        {
            _ = await request.Content!.ReadAsByteArrayAsync();
            return originalResponse;
        });
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions(),
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = new StreamContent(new MemoryStream([1, 2, 3])),
        };
        request.Content.Headers.ContentLength = 3;

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(ReferenceEquals(response, originalResponse)).IsTrue();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("original");
        await Assert.That(transport.Attempts).IsEqualTo(1);
        await Assert.That(responseContent.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Put_With_ByteArrayContent_NoBuffer_Is_Retried()
    {
        var bodies = new List<byte[]>();
        var transport = new RecordingHandler(async (attempt, request, _) =>
        {
            bodies.Add(await request.Content!.ReadAsByteArrayAsync());
            return new HttpResponseMessage(
                attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions(),
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = new ByteArrayContent([1, 2, 3]),
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(transport.Attempts).IsEqualTo(2);
        await Assert.That(bodies.All(static body => body.SequenceEqual(new byte[] { 1, 2, 3 }))).IsTrue();
    }

    [Test]
    public async Task ReReadable_NoBuffer_Content_Is_Retried()
    {
        Func<HttpContent>[] factories =
        [
            static () => new StringContent("payload"),
            static () => new FormUrlEncodedContent([new("name", "value")]),
        ];

        foreach (var factory in factories)
        {
            var bodies = new List<string>();
            var transport = new RecordingHandler(async (attempt, request, _) =>
            {
                bodies.Add(await request.Content!.ReadAsStringAsync());
                return new HttpResponseMessage(
                    attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            });
            using var invoker = CreateInvoker(
                HttpShield.WhenTransient().Retry(1, Backoff.None),
                new ShieldHttpHandlerOptions(),
                transport);
            using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
            {
                Content = factory(),
            };

            using var response = await invoker.SendAsync(request, CancellationToken.None);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(transport.Attempts).IsEqualTo(2);
            await Assert.That(bodies[1]).IsEqualTo(bodies[0]);
        }
    }

    [Test]
    public async Task JsonContent_With_Consumable_Value_NoBuffer_Returns_Original_Response()
    {
        var values = new SingleUseAsyncEnumerable();
        var originalResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var transport = new RecordingHandler(async (_, request, _) =>
        {
            await request.Content!.CopyToAsync(Stream.Null);
            return originalResponse;
        });
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions(),
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = JsonContent.Create<IAsyncEnumerable<int>>(values),
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(ReferenceEquals(response, originalResponse)).IsTrue();
        await Assert.That(transport.Attempts).IsEqualTo(1);
        await Assert.That(values.Enumerations).IsEqualTo(1);
    }

    [Test]
    public async Task Buffered_JsonContent_NoBuffer_Is_Retried()
    {
        var bodies = new List<string>();
        var transport = new RecordingHandler(async (attempt, request, _) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(
                attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions(),
            transport);
        using var content = JsonContent.Create(new { Name = "value" });
        await content.LoadIntoBufferAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = content,
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(transport.Attempts).IsEqualTo(2);
        await Assert.That(bodies[1]).IsEqualTo(bodies[0]);
    }

    [Test]
    public async Task Post_With_AllowUnsafeMethodReplay_Is_Retried()
    {
        var transport = new RecordingHandler((attempt, _, _) => Task.FromResult(
            new HttpResponseMessage(
                attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions { AllowUnsafeMethodReplay = true },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/upload")
        {
            Content = new StringContent("payload"),
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(transport.Attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Already_Buffered_StreamContent_NoBuffer_Is_Retried()
    {
        var bodies = new List<byte[]>();
        var transport = new RecordingHandler(async (attempt, request, _) =>
        {
            bodies.Add(await request.Content!.ReadAsByteArrayAsync());
            return new HttpResponseMessage(
                attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions(),
            transport);
        using var content = new StreamContent(new MemoryStream([1, 2, 3]));
        await content.LoadIntoBufferAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = content,
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(transport.Attempts).IsEqualTo(2);
        await Assert.That(bodies.All(static body => body.SequenceEqual(new byte[] { 1, 2, 3 }))).IsTrue();
    }

    [Test]
    public async Task RequestFactory_Opts_Post_Into_Fresh_PerAttempt_Content()
    {
        var createdContent = new List<TrackingContent>();
        var transport = new RecordingHandler((attempt, request, _) => Task.FromResult(
            new HttpResponseMessage(attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        var options = new ShieldHttpHandlerOptions
        {
            RequestFactory = (original, attempt, _) =>
            {
                var content = new TrackingContent($"attempt-{attempt}");
                createdContent.Add(content);
                return new ValueTask<HttpRequestMessage>(new HttpRequestMessage(original.Method, original.RequestUri)
                {
                    Content = content,
                });
            },
        };
        using var invoker = CreateInvoker(HttpShield.WhenTransient().Retry(1, Backoff.None), options, transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/write");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(createdContent.Count).IsEqualTo(2);
        await Assert.That(createdContent.All(static content => content.DisposeCount == 1)).IsTrue();
    }

    [Test]
    public async Task Buffer_Limit_Fails_Before_Transport_After_Partial_Serialization()
    {
        var transport = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(
            Shield<HttpResponseMessage>.Empty,
            new ShieldHttpHandlerOptions
            {
                ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
                MaximumBufferSize = 4,
            },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = new PartialFailureContent(),
        };

        _ = await Assert.That(async () =>
                await invoker.SendAsync(request, CancellationToken.None))
            .Throws<HttpRequestReplayException>();

        await Assert.That(transport.Attempts).IsEqualTo(0);
    }

    [Test]
    public async Task Declared_Content_Length_Over_Limit_Fails_Before_Serialization_Or_Transport()
    {
        var content = new SerializationTrackingContent(contentLength: 5);
        var transport = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(
            Shield<HttpResponseMessage>.Empty,
            new ShieldHttpHandlerOptions
            {
                ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
                MaximumBufferSize = 4,
            },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = content,
        };

        var exception = await Assert.That(async () =>
                await invoker.SendAsync(request, CancellationToken.None))
            .Throws<HttpRequestReplayException>();

        await Assert.That(exception!.Message).Contains("4-byte replay buffer limit");
        await Assert.That(content.SerializationAttempts).IsEqualTo(0);
        await Assert.That(transport.Attempts).IsEqualTo(0);
    }

    [Test]
    public async Task Null_RequestFactory_Result_Fails_Actionably_Before_Transport()
    {
        var transport = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(
            Shield<HttpResponseMessage>.Empty,
            new ShieldHttpHandlerOptions
            {
                RequestFactory = static (_, _, _) =>
                    new ValueTask<HttpRequestMessage>((HttpRequestMessage)null!),
            },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/api");

        var exception = await Assert.That(async () =>
                await invoker.SendAsync(request, CancellationToken.None))
            .Throws<HttpRequestReplayException>();

        await Assert.That(exception!.Message).Contains("RequestFactory returned null");
        await Assert.That(transport.Attempts).IsEqualTo(0);
    }

    [Test]
    public async Task Caller_Cancellation_Interrupts_Request_Buffering()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new CancellationAwareContent();
        var transport = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(
            Shield<HttpResponseMessage>.Empty,
            new ShieldHttpHandlerOptions { ContentReplayPolicy = HttpContentReplayPolicy.Buffer },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = content,
        };

        var send = invoker.SendAsync(request, cancellation.Token);
        await content.Started.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var exception = await Assert.That(async () => await send).Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await content.Cancelled.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(transport.Attempts).IsEqualTo(0);
    }

    [Test]
    public async Task Shield_Timeout_Includes_Request_Buffering()
    {
        var content = new CancellationAwareContent();
        var transport = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(
            Shield.Timeout(TimeSpan.FromMilliseconds(20)).For<HttpResponseMessage>(),
            new ShieldHttpHandlerOptions { ContentReplayPolicy = HttpContentReplayPolicy.Buffer },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = content,
        };

        _ = await Assert.That(async () => await invoker.SendAsync(request, CancellationToken.None))
            .Throws<TimeoutExceededException>();

        await content.Cancelled.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(transport.Attempts).IsEqualTo(0);
    }

    [Test]
    public async Task Hedge_Attempt_Timeout_Does_Not_Cancel_Shared_Buffering()
    {
        var content = new GatedContent("payload");
        var hedgeLaunched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptTimedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var policy = HttpShield.WhenTransient()
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = TimeSpan.FromMilliseconds(250);
                options.OnHedgeAsync = async _ =>
                {
                    hedgeLaunched.TrySetResult();
                    await attemptTimedOut.Task;
                };
            })
            .Timeout(options =>
            {
                options.Timeout = TimeSpan.FromMilliseconds(500);
                options.OnTimeout = _ => attemptTimedOut.TrySetResult();
            });
        using var invoker = CreateInvoker(
            policy,
            new ShieldHttpHandlerOptions { ContentReplayPolicy = HttpContentReplayPolicy.Buffer },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = content,
        };

        var send = invoker.SendAsync(request, CancellationToken.None);
        await content.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await hedgeLaunched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await attemptTimedOut.Task.WaitAsync(TimeSpan.FromSeconds(5));
        content.Release();

        using var response = await send.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content.SerializationAttempts).IsEqualTo(1);
        await Assert.That(transport.Attempts).IsEqualTo(1);
    }

    [Test]
    public async Task PreCancellation_Skips_RequestFactory_And_Transport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factoryCalls = 0;
        var transport = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(
            Shield<HttpResponseMessage>.Empty,
            new ShieldHttpHandlerOptions
            {
                RequestFactory = (original, _, _) =>
                {
                    factoryCalls++;
                    return new ValueTask<HttpRequestMessage>(
                        new HttpRequestMessage(original.Method, original.RequestUri));
                },
            },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/api");

        _ = await Assert.That(async () =>
                await invoker.SendAsync(request, cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(factoryCalls).IsEqualTo(0);
        await Assert.That(transport.Attempts).IsEqualTo(0);
    }

    [Test]
    public async Task Timeout_Disposes_RequestFactory_Content()
    {
        var content = new TrackingContent("payload");
        var transport = new RecordingHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var invoker = CreateInvoker(
            Shield.Timeout(TimeSpan.FromMilliseconds(20)).For<HttpResponseMessage>(),
            new ShieldHttpHandlerOptions
            {
                RequestFactory = (original, _, _) => new ValueTask<HttpRequestMessage>(
                    new HttpRequestMessage(original.Method, original.RequestUri) { Content = content }),
            },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/api");

        _ = await Assert.That(async () =>
                await invoker.SendAsync(request, CancellationToken.None))
            .Throws<TimeoutExceededException>();

        await Assert.That(content.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Ordered_Hedge_Routes_To_Different_Authorities_And_Disposes_Loser()
    {
        var destinations = new List<(string Host, string PathAndQuery)>();
        var loserContent = new TrackingContent("loser");
        var transport = new RecordingHandler(async (_, request, cancellationToken) =>
        {
            lock (destinations)
            {
                destinations.Add((request.RequestUri!.Host, request.RequestUri.PathAndQuery));
            }

            if (request.RequestUri!.Host == "first.example")
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = loserContent };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var options = RoutingOptions(
            HttpEndpointSelectionMode.Ordered,
            new HttpEndpoint(new Uri("https://first.example")),
            new HttpEndpoint(new Uri("https://second.example")));
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Hedge(2, TimeSpan.Zero),
            options,
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/path?q=1");

        using var response = await invoker.SendAsync(request, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(destinations.Take(2).Select(static item => item.Host)
            .SequenceEqual(["first.example", "second.example"])).IsTrue();
        await Assert.That(destinations.All(static item => item.PathAndQuery == "/path?q=1")).IsTrue();
        await loserContent.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(loserContent.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Standard_Hedging_Routes_And_Isolates_Endpoint_Breakers()
    {
        var calls = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(
            StringComparer.Ordinal);
        var transport = new RecordingHandler((_, request, _) =>
        {
            var host = request.RequestUri!.Host;
            _ = calls.AddOrUpdate(host, 1, static (_, current) => current + 1);
            return Task.FromResult(new HttpResponseMessage(
                host == "first.example" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        });
        using var services = CreateStandardHedgingServices(transport, options =>
        {
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.example")));
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.example")));
            options.HedgeDelay = Timeout.InfiniteTimeSpan;
            options.ConsecutiveFailures = 1;
            options.FailureRatio = null;
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("standard-hedging");

        using var first = await client.GetAsync("https://origin.example/api");
        using var second = await client.GetAsync("https://origin.example/api");

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(calls["first.example"]).IsEqualTo(1);
        await Assert.That(calls["second.example"]).IsEqualTo(2);
    }

    [Test]
    public async Task Standard_Hedging_Attempt_Timeout_Advances_To_Next_Endpoint()
    {
        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new RecordingHandler(async (_, request, cancellationToken) =>
        {
            if (request.RequestUri!.Host == "first.example")
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    timedOut.TrySetResult();
                    throw;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var services = CreateStandardHedgingServices(transport, options =>
        {
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.example")));
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.example")));
            options.HedgeDelay = Timeout.InfiniteTimeSpan;
            options.AttemptTimeout = TimeSpan.FromMilliseconds(20);
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("standard-hedging");

        using var response = await client.GetAsync("https://origin.example/api")
            .WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(transport.Attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Standard_Hedging_Propagates_Caller_Cancellation()
    {
        var transport = new RecordingHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var services = CreateStandardHedgingServices(transport, options =>
        {
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.example")));
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.example")));
            options.HedgeDelay = TimeSpan.Zero;
            options.TotalTimeout = TimeSpan.FromMinutes(1);
            options.AttemptTimeout = TimeSpan.FromMinutes(1);
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("standard-hedging");
        using var cancellation = new CancellationTokenSource();

        var send = client.GetAsync("https://origin.example/api", cancellation.Token);
        await Task.Delay(20);
        cancellation.Cancel();

        var exception = await Assert.That(async () => await send).Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Standard_Hedging_Total_Timeout_Covers_All_Attempts()
    {
        var transport = new RecordingHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var services = CreateStandardHedgingServices(transport, options =>
        {
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.example")));
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.example")));
            options.HedgeDelay = TimeSpan.Zero;
            options.TotalTimeout = TimeSpan.FromSeconds(1);
            options.AttemptTimeout = TimeSpan.FromMinutes(1);
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("standard-hedging");

        _ = await Assert.That(async () => await client.GetAsync("https://origin.example/api"))
            .Throws<TimeoutExceededException>();

        await Assert.That(transport.Attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Standard_Hedging_Explicitly_Replays_Unsafe_Content_And_Disposes_Failure()
    {
        var failedContent = new TrackingContent("failed");
        var bodies = new List<string>();
        var transport = new RecordingHandler(async (_, request, _) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return request.RequestUri!.Host == "first.example"
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = failedContent }
                : new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var services = CreateStandardHedgingServices(transport, options =>
        {
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.example")));
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.example")));
            options.HedgeDelay = Timeout.InfiniteTimeSpan;
            options.ContentReplayPolicy = HttpContentReplayPolicy.Buffer;
            options.AllowUnsafeMethodReplay = true;
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("standard-hedging");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/upload")
        {
            Content = new StringContent("payload"),
        };

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(bodies).IsEquivalentTo(["payload", "payload"]);
        await Assert.That(failedContent.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Non_Replayable_Request_Under_Hedging_Sends_Once()
    {
        var failedContent = new TrackingContent("failed");
        var transport = new RecordingHandler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = failedContent }));
        using var services = CreateStandardHedgingServices(transport, options =>
        {
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.example")));
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.example")));
            options.HedgeDelay = Timeout.InfiniteTimeSpan;
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("standard-hedging");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/upload")
        {
            Content = new StringContent("payload"),
        };

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(transport.Attempts).IsEqualTo(1);
        await Assert.That(failedContent.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Standard_Hedging_Uses_Second_Endpoint_When_First_Is_Concurrency_Limited()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hosts = new System.Collections.Concurrent.ConcurrentBag<string>();
        var transport = new RecordingHandler(async (_, request, cancellationToken) =>
        {
            hosts.Add(request.RequestUri!.Host);
            if (request.RequestUri.Host == "first.example")
            {
                firstStarted.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var services = CreateStandardHedgingServices(transport, options =>
        {
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.example")));
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.example")));
            options.HedgeDelay = Timeout.InfiniteTimeSpan;
            options.MaxConcurrency = 1;
            options.QueueLimit = 0;
            options.TotalTimeout = TimeSpan.FromMinutes(1);
            options.AttemptTimeout = TimeSpan.FromMinutes(1);
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("standard-hedging");

        var holding = client.GetAsync("https://origin.example/holding");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var overflow = await client.GetAsync("https://origin.example/overflow")
            .WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();
        using var held = await holding.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(overflow.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(held.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(hosts.Count(static host => host == "first.example")).IsEqualTo(1);
        await Assert.That(hosts.Count(static host => host == "second.example")).IsEqualTo(1);
    }

    [Test]
    public async Task Standard_Hedging_Uses_Weighted_Endpoint_Selection()
    {
        var hosts = new List<string>();
        var transport = new RecordingHandler((_, request, _) =>
        {
            hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var services = CreateStandardHedgingServices(transport, options =>
        {
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://first.example"), 5));
            options.Endpoints.Add(new HttpEndpoint(new Uri("https://second.example"), 1));
            options.SelectionMode = HttpEndpointSelectionMode.Weighted;
            options.Seed = 1729;
            options.MaxAttempts = 1;
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("standard-hedging");

        for (var index = 0; index < 32; index++)
        {
            using var response = await client.GetAsync($"https://origin.example/{index}");
        }

        await Assert.That(hosts.Distinct().Count()).IsEqualTo(2);
        await Assert.That(hosts.Count(static host => host == "first.example"))
            .IsGreaterThan(hosts.Count(static host => host == "second.example"));
    }

    [Test]
    public async Task Weighted_Routing_Is_Deterministic_For_A_Seed()
    {
        var first = await WeightedSequence(1729);
        var second = await WeightedSequence(1729);

        await Assert.That(first.SequenceEqual(second)).IsTrue();
        await Assert.That(first.Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Weighted_Routing_Advances_Primary_Selection_Between_Requests()
    {
        var hosts = new List<string>();
        var transport = new RecordingHandler((_, request, _) =>
        {
            hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var options = RoutingOptions(
            HttpEndpointSelectionMode.Weighted,
            new HttpEndpoint(new Uri("https://first.example"), 5),
            new HttpEndpoint(new Uri("https://second.example"), 2),
            new HttpEndpoint(new Uri("https://third.example"), 1));
        options.Routing!.Seed = 1729;
        using var invoker = CreateInvoker(Shield<HttpResponseMessage>.Empty, options, transport);

        for (var index = 0; index < 32; index++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://origin.example/{index}");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
        }

        await Assert.That(hosts.Distinct().Count()).IsGreaterThan(1);
        await Assert.That(hosts.Count(static host => host == "first.example"))
            .IsGreaterThan(hosts.Count(static host => host == "third.example"));
    }

    [Test]
    public async Task Endpoint_Shields_Isolate_Circuit_State_By_Authority()
    {
        var calls = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(
            StringComparer.Ordinal);
        var transport = new RecordingHandler((_, request, _) =>
        {
            var host = request.RequestUri!.Host;
            _ = calls.AddOrUpdate(host, 1, static (_, current) => current + 1);
            return Task.FromResult(new HttpResponseMessage(
                host == "first.example" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        });
        var options = RoutingOptions(
            HttpEndpointSelectionMode.Ordered,
            new HttpEndpoint(new Uri("https://first.example")),
            new HttpEndpoint(new Uri("https://second.example")));
        options.Routing!.ShieldFactory = _ =>
            HttpShield.WhenTransient().CircuitBreaker(1, TimeSpan.FromHours(1));
        var routingPolicy = Shield.For<HttpResponseMessage>()
            .When<CircuitOpenException>()
            .OrResult(HttpShield.IsTransient)
            .Hedge(2, TimeSpan.Zero);
        using var invoker = CreateInvoker(routingPolicy, options, transport);

        for (var requestIndex = 0; requestIndex < 2; requestIndex++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/api");
            using var response = await invoker.SendAsync(request, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        await Assert.That(calls["first.example"]).IsEqualTo(1);
        await Assert.That(calls["second.example"]).IsEqualTo(2);
    }

    [Test]
    public async Task Routed_NonReplayable_Content_Uses_Only_First_Endpoint()
    {
        var bodies = new List<string>();
        var hosts = new List<string>();
        var contentHeaderCounts = new List<int>();
        var content = new TrackingContent("payload");
        var transport = new RecordingHandler(async (_, request, _) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            hosts.Add(request.RequestUri!.Host);
            contentHeaderCounts.Add(request.Content.Headers.GetValues("x-content").Count());
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        var options = RoutingOptions(
            HttpEndpointSelectionMode.Ordered,
            new HttpEndpoint(new Uri("https://first.example")),
            new HttpEndpoint(new Uri("https://second.example")));
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            options,
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = content,
        };
        request.Content.Headers.TryAddWithoutValidation("x-content", new[] { "one", "two" });

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(bodies).IsEquivalentTo(["payload"]);
        await Assert.That(hosts).IsEquivalentTo(["first.example"]);
        await Assert.That(contentHeaderCounts).IsEquivalentTo([2]);
        await Assert.That(transport.Attempts).IsEqualTo(1);
        await Assert.That(content.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task HttpRequestException_On_NonReplayable_Request_Is_Rethrown_Unchanged()
    {
        var expected = new HttpRequestException("original");
        var transport = new RecordingHandler((_, _, _) => throw expected);
        using var invoker = CreateInvoker(
            HttpShield.WhenTransient().Retry(3, Backoff.Constant(TimeSpan.FromSeconds(1))),
            new ShieldHttpHandlerOptions(),
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/upload")
        {
            Content = new StringContent("payload"),
        };

        var actual = await Assert.That(async () =>
                await invoker.SendAsync(request, CancellationToken.None))
            .Throws<HttpRequestException>();

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(transport.Attempts).IsEqualTo(1);
    }

    [Test]
    public async Task NonReplayable_Request_Counts_As_CircuitBreaker_Failure()
    {
        var transport = new RecordingHandler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var shield = HttpShield.WhenTransient()
            .Retry(3, Backoff.None)
            .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromHours(1));
        using var invoker = CreateInvoker(shield, new ShieldHttpHandlerOptions(), transport);

        for (var requestIndex = 0; requestIndex < 2; requestIndex++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/upload")
            {
                Content = new StringContent("payload"),
            };
            using var response = await invoker.SendAsync(request, CancellationToken.None);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }

        using var blockedRequest = new HttpRequestMessage(HttpMethod.Post, "https://origin.example/upload")
        {
            Content = new StringContent("payload"),
        };
        _ = await Assert.That(async () =>
                await invoker.SendAsync(blockedRequest, CancellationToken.None))
            .Throws<CircuitOpenException>();
        await Assert.That(transport.Attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Failed_Buffering_Is_Not_Cached_Across_Retries()
    {
        var content = new FlakyBufferContent();
        var transport = new RecordingHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(
            Shield.For<HttpResponseMessage>()
                .When<HttpRequestReplayException>()
                .Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions { ContentReplayPolicy = HttpContentReplayPolicy.Buffer },
            transport);
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://origin.example/upload")
        {
            Content = content,
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content.SerializationAttempts).IsEqualTo(2);
        await Assert.That(transport.Attempts).IsEqualTo(1);
    }

    [Test]
    public async Task All_Failed_Hedges_Keep_Final_Response_Usable_And_Evaluate_Once()
    {
        var responses = new System.Collections.Concurrent.ConcurrentBag<
            (HttpResponseMessage Response, TrackingContent Content)>();
        var predicateCalls = 0;
        var transport = new RecordingHandler((attempt, _, _) =>
        {
            var content = new TrackingContent($"attempt-{attempt}");
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = content,
            };
            responses.Add((response, content));
            return Task.FromResult(response);
        });
        var policy = Shield.For<HttpResponseMessage>()
            .WhenResult(_ =>
            {
                Interlocked.Increment(ref predicateCalls);
                return true;
            })
            .Hedge(3, TimeSpan.Zero);
        using var invoker = CreateInvoker(policy, new ShieldHttpHandlerOptions(), transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/api");

        using var response = await invoker.SendAsync(request, CancellationToken.None);
        var selected = responses.Single(item => ReferenceEquals(item.Response, response));

        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("attempt-");
        await Assert.That(predicateCalls).IsEqualTo(3);
        await Assert.That(selected.Content.DisposeCount).IsEqualTo(0);
        await Assert.That(responses.Where(item => !ReferenceEquals(item.Response, response))
            .All(static item => item.Content.DisposeCount == 1)).IsTrue();
    }

    private static async Task<string[]> WeightedSequence(int seed)
    {
        var hosts = new List<string>();
        var transport = new RecordingHandler((attempt, request, _) =>
        {
            hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(
                attempt < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        });
        var options = RoutingOptions(
            HttpEndpointSelectionMode.Weighted,
            new HttpEndpoint(new Uri("https://first.example"), 5),
            new HttpEndpoint(new Uri("https://second.example"), 2),
            new HttpEndpoint(new Uri("https://third.example"), 1));
        options.Routing!.Seed = seed;
        using var invoker = CreateInvoker(HttpShield.WhenTransient().Retry(2, Backoff.None), options, transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://origin.example/api");
        using var response = await invoker.SendAsync(request, CancellationToken.None);
        return hosts.ToArray();
    }

    private static ShieldHttpHandlerOptions RoutingOptions(
        HttpEndpointSelectionMode selectionMode,
        params HttpEndpoint[] endpoints)
    {
        var routing = new HttpEndpointRoutingOptions { SelectionMode = selectionMode };
        foreach (var endpoint in endpoints)
        {
            routing.Endpoints.Add(endpoint);
        }

        return new ShieldHttpHandlerOptions { Routing = routing };
    }

    private static ServiceProvider CreateStandardHedgingServices(
        HttpMessageHandler transport,
        Action<StandardHedgingShieldOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("standard-hedging")
            .ConfigurePrimaryHttpMessageHandler(() => transport)
            .AddStandardHedgingShield(configure);
        return services.BuildServiceProvider();
    }

    private static HttpMessageInvoker CreateInvoker(
        Shield<HttpResponseMessage> shield,
        ShieldHttpHandlerOptions options,
        HttpMessageHandler transport) =>
        new(new ShieldDelegatingHandler(shield, options) { InnerHandler = transport });

    private sealed class RecordingHandler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(Interlocked.Increment(ref _attempts), request, cancellationToken);
    }

    private sealed class SingleUseAsyncEnumerable : IAsyncEnumerable<int>
    {
        private int _enumerations;

        public int Enumerations => Volatile.Read(ref _enumerations);

        public async IAsyncEnumerator<int> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _enumerations) != 1)
            {
                throw new InvalidOperationException("The sequence can only be consumed once.");
            }

            yield return 1;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return 2;
        }
    }

    private sealed class TrackingContent(string value) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(value);
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task WaitForDisposalAsync() => _disposed.Task;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes, 0, _bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
                _disposed.TrySetResult();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class SerializationTrackingContent(long contentLength) : HttpContent
    {
        private int _serializationAttempts;

        public int SerializationAttempts => Volatile.Read(ref _serializationAttempts);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Interlocked.Increment(ref _serializationAttempts);
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = contentLength;
            return true;
        }
    }

    private sealed class PartialFailureContent : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(new byte[] { 1, 2, 3 });
            throw new IOException("serialization failed");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class FlakyBufferContent : HttpContent
    {
        private int _serializationAttempts;

        public int SerializationAttempts => Volatile.Read(ref _serializationAttempts);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            if (Interlocked.Increment(ref _serializationAttempts) == 1)
            {
                throw new IOException("first buffering attempt failed");
            }

            await stream.WriteAsync(new byte[] { 1, 2, 3 });
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class CancellationAwareContent : HttpContent
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public Task Cancelled => _cancelled.Task;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cancelled.TrySetResult();
                throw;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class GatedContent(string value) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(value);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _serializationAttempts;

        public int SerializationAttempts => Volatile.Read(ref _serializationAttempts);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _serializationAttempts);
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            await stream.WriteAsync(_bytes, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }
    }
}

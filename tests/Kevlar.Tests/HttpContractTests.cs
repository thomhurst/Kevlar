using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Kevlar.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class HttpContractTests
{
    [Test]
    public async Task Endpoint_Value_Object_Validates_And_Preserves_Inputs()
    {
        var uri = new Uri("https://api.example:8443/base");
        var endpoint = new HttpEndpoint(uri, weight: 7);

        await Assert.That(ReferenceEquals(endpoint.Uri, uri)).IsTrue();
        await Assert.That(endpoint.Weight).IsEqualTo(7);

        var nullUri = await Assert.That(() => new HttpEndpoint(null!))
            .Throws<ArgumentNullException>();
        var relativeUri = await Assert.That(() => new HttpEndpoint(new Uri("relative", UriKind.Relative)))
            .Throws<ArgumentException>();
        var zeroWeight = await Assert.That(() => new HttpEndpoint(uri, weight: 0))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(nullUri!.ParamName).IsEqualTo("uri");
        await Assert.That(relativeUri!.ParamName).IsEqualTo("uri");
        await Assert.That(zeroWeight!.ParamName).IsEqualTo("weight");
    }

    [Test]
    public async Task Replay_Exception_Constructors_Preserve_Message_And_Inner_Exception()
    {
        var inner = new IOException("serialization failed");
        var simple = new HttpRequestReplayException("replay failed");
        var wrapped = new HttpRequestReplayException("buffer failed", inner);

        await Assert.That(simple.Message).IsEqualTo("replay failed");
        await Assert.That(simple.InnerException).IsNull();
        await Assert.That(wrapped.Message).IsEqualTo("buffer failed");
        await Assert.That(ReferenceEquals(wrapped.InnerException, inner)).IsTrue();
    }

    [Test]
    [Arguments(200, false)]
    [Arguments(301, false)]
    [Arguments(400, false)]
    [Arguments(408, true)]
    [Arguments(429, true)]
    [Arguments(499, false)]
    [Arguments(500, true)]
    [Arguments(599, true)]
    [Arguments(600, false)]
    [Arguments(999, false)]
    public async Task IsTransient_Recognizes_Only_Documented_Statuses(int statusCode, bool expected)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        await Assert.That(HttpShield.IsTransient(response)).IsEqualTo(expected);
    }

    [Test]
    public async Task IsTransient_Rejects_Null() =>
        await Assert.That(HttpShield.IsTransient(null!)).IsFalse();

    [Test]
    [Arguments(typeof(HttpRequestException))]
    [Arguments(typeof(TaskCanceledException))]
    [Arguments(typeof(TimeoutExceededException))]
    public async Task WhenTransient_Retries_Documented_Exceptions(Type exceptionType)
    {
        var calls = 0;
        using var inner = new DelegateHandler((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw exceptionType == typeof(HttpRequestException)
                    ? new HttpRequestException("transient")
                    : exceptionType == typeof(TaskCanceledException)
                        ? new TaskCanceledException("HttpClient timeout", new TimeoutException())
                        : new TimeoutExceededException(TimeSpan.FromSeconds(1));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = CreateClient(inner, HttpShield.WhenTransient().Retry(1, Backoff.None));

        using var response = await client.GetAsync("http://localhost/test");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task IsTransientException_Distinguishes_Timeouts_From_Caller_Cancellation()
    {
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        await Assert.That(HttpShield.IsTransientException(
            new HttpRequestException("socket", new System.Net.Sockets.SocketException()),
            CancellationToken.None)).IsTrue();
        await Assert.That(HttpShield.IsTransientException(
            new TaskCanceledException("HttpClient timeout", new TimeoutException()),
            CancellationToken.None)).IsTrue();
        await Assert.That(HttpShield.IsTransientException(
            new TaskCanceledException(),
            CancellationToken.None)).IsFalse();
        await Assert.That(HttpShield.IsTransientException(
            new TimeoutExceededException(TimeSpan.FromSeconds(1)),
            CancellationToken.None)).IsTrue();
        await Assert.That(HttpShield.IsTransientException(
            new TaskCanceledException("caller", null, callerCancellation.Token),
            callerCancellation.Token)).IsFalse();
        await Assert.That(HttpShield.IsTransientException(
            new OperationCanceledException(callerCancellation.Token),
            callerCancellation.Token)).IsFalse();
        await Assert.That(HttpShield.IsTransientException(
            new InvalidOperationException(),
            CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task WhenTransient_Does_Not_Retry_Caller_Cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var retries = 0;
        using var inner = new DelegateHandler((_, _) =>
        {
            calls++;
            throw new TaskCanceledException("caller", null, cancellation.Token);
        });
        var shield = HttpShield.WhenTransient().Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => retries++;
        });
        using var client = CreateClient(inner, shield);

        _ = await Assert.That(async () => await client.GetAsync("http://localhost/test"))
            .Throws<TaskCanceledException>();

        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(retries).IsEqualTo(0);
    }

    [Test]
    public async Task WhenTransient_Breaker_Counts_HttpClient_Timeouts()
    {
        var calls = 0;
        var shield = HttpShield.WhenTransient()
            .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromMinutes(1));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var outcome = await shield.ExecuteOutcomeAsync<HttpResponseMessage>(_ =>
            {
                calls++;
                throw new TaskCanceledException("HttpClient timeout", new TimeoutException());
            });
            await Assert.That(outcome.Exception).IsTypeOf<TaskCanceledException>();
        }

        var rejected = await shield.ExecuteOutcomeAsync(
            _ => new ValueTask<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK)));
        await Assert.That(rejected.Exception).IsTypeOf<CircuitOpenException>();
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task WhenTransient_Describes_HttpClient_Timeouts() =>
        await Assert.That(HttpShield.WhenTransient().Retry(1, Backoff.None).ToString()).IsEqualTo(
            "[when HttpRequestException | TaskCanceledException matching predicate | TimeoutExceededException | result predicate] "
            + "Retry(1, no delay)");

    [Test]
    public async Task WhenTransient_Retries_Real_HttpClient_Timeout()
    {
        var calls = 0;
        using var inner = new DelegateHandler(async (_, cancellationToken) =>
        {
            calls++;
            if (calls == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(inner, disposeHandler: false)
        {
            Timeout = TimeSpan.FromMilliseconds(25),
        };
        var shield = HttpShield.WhenTransient().Retry(1, Backoff.None);

        using var response = await shield.ExecuteAsync(
            cancellationToken => new ValueTask<HttpResponseMessage>(
                client.GetAsync("http://localhost/test", cancellationToken)));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Standard_Uses_Attempt_Timeout_Instead_Of_HttpClient_Timeout()
    {
        var calls = 0;
        using var inner = new DelegateHandler(async (_, cancellationToken) =>
        {
            calls++;
            if (calls == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var services = new ServiceCollection()
            .AddHttpClient("standard-timeout", client =>
                client.Timeout = TimeSpan.FromMilliseconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => inner)
            .AddStandardShield(options =>
            {
                options.TotalTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.Retry.MaxRetries = 1;
                options.Retry.Backoff = Backoff.None;
                options.CircuitBreaker.ConsecutiveFailures = 100;
                options.CircuitBreaker.FailureRatio = null;
                options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(50);
            })
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("standard-timeout");

        using var response = await client.GetAsync("http://localhost/test");

        await Assert.That(client.Timeout).IsEqualTo(Timeout.InfiniteTimeSpan);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Standard_Hedge_Uses_Shield_Timeout_Instead_Of_HttpClient_Timeout()
    {
        using var services = new ServiceCollection()
            .AddHttpClient("standard-hedge-timeout", client =>
                client.Timeout = TimeSpan.FromMilliseconds(10))
            .AddStandardHedgeShield(options =>
                options.Endpoints.Add(new HttpEndpoint(new Uri("https://example.test"))))
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("standard-hedge-timeout");

        await Assert.That(client.Timeout).IsEqualTo(Timeout.InfiniteTimeSpan);
    }

    [Test]
    [Arguments(RetryAfterKind.Absent, 3)]
    [Arguments(RetryAfterKind.ShorterDelta, 3)]
    [Arguments(RetryAfterKind.EqualDelta, 3)]
    [Arguments(RetryAfterKind.LongerDelta, 7)]
    [Arguments(RetryAfterKind.PastDate, 3)]
    [Arguments(RetryAfterKind.CurrentDate, 3)]
    [Arguments(RetryAfterKind.LongerDate, 7)]
    public async Task RetryAfter_Uses_Only_Longer_Server_Delays(RetryAfterKind kind, int expectedSeconds)
    {
        var now = new DateTimeOffset(2035, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = CreateRetryAfter(kind, now);

        var delay = await ObserveRetryDelay(timeProvider, () => response);

        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    public async Task RetryAfter_Ignores_Exception_Outcomes()
    {
        var delay = await ObserveRetryDelay(
            new FakeTimeProvider(),
            () => throw new HttpRequestException("transient"));

        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task RetryAfter_Max_Overload_Caps_Server_Delay()
    {
        var timeProvider = new FakeTimeProvider();
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        var delay = await ObserveRetryDelay(
            timeProvider,
            () => response,
            HttpShield.RetryAfter(TimeSpan.FromSeconds(5)));

        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task RetryAfter_Max_Overload_Does_Not_Shorten_Backoff()
    {
        var timeProvider = new FakeTimeProvider();
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        var delay = await ObserveRetryDelay(
            timeProvider,
            () => response,
            HttpShield.RetryAfter(TimeSpan.FromSeconds(5)),
            backoff: TimeSpan.FromSeconds(7));

        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(7));
    }

    [Test]
    public async Task RetryAfter_Max_Overload_Rejects_Negative_Cap()
    {
        var exception = await Assert.That(
            () => HttpShield.RetryAfter(TimeSpan.FromTicks(-1)))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(exception!.ParamName).IsEqualTo("maxDelay");
    }

    [Test]
    public async Task Standard_Caps_RetryAfter_To_Ten_Seconds()
    {
        var timeProvider = new FakeTimeProvider();
        var calls = 0;
        TimeSpan? observed = null;
        var options = new StandardHttpShieldOptions();
        options.Retry.Backoff = Backoff.None;
        options.Retry.OnRetry = retry => observed = retry.Delay;
        var shield = HttpShield.Standard(options).WithTimeProvider(timeProvider);

        var execution = shield.ExecuteAsync(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(
                calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (calls == 1)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            }

            return new ValueTask<HttpResponseMessage>(response);
        }).AsTask();

        await WaitUntilAsync(() => observed.HasValue);
        await Assert.That(observed).IsEqualTo(TimeSpan.FromSeconds(10));
        timeProvider.Advance(observed!.Value);

        using var result = await execution;
        await Assert.That(result.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Replacing_Retry_Options_Keeps_RetryAfter()
    {
        var options = new StandardHttpShieldOptions
        {
            Retry = new RetryOptions<HttpResponseMessage>
            {
                MaxRetries = 1,
                Backoff = Backoff.None,
            },
        };

        var delay = await ObserveStandardRetryDelay(
            options,
            retryAfter: TimeSpan.FromSeconds(2));

        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task UseRetryAfterHeader_False_Ignores_Header()
    {
        var options = new StandardHttpShieldOptions
        {
            UseRetryAfterHeader = false,
            Retry = new RetryOptions<HttpResponseMessage>
            {
                MaxRetries = 1,
                Backoff = Backoff.Constant(TimeSpan.FromSeconds(3)),
            },
        };

        var delay = await ObserveStandardRetryDelay(
            options,
            retryAfter: TimeSpan.FromSeconds(7));

        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(3));
    }

    [Test]
    [Arguments(2, 5)]
    [Arguments(7, 7)]
    public async Task Custom_DelayGenerator_Composes_With_RetryAfter(
        int retryAfterSeconds,
        int expectedSeconds)
    {
        var options = new StandardHttpShieldOptions();
        options.Retry.MaxRetries = 1;
        options.Retry.Backoff = Backoff.None;
        options.Retry.DelayGenerator = static _ => TimeSpan.FromSeconds(5);

        var delay = await ObserveStandardRetryDelay(
            options,
            retryAfter: TimeSpan.FromSeconds(retryAfterSeconds));

        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    public async Task Standard_Default_Retry_MaxDelay_Is_Ten_Seconds() =>
        await Assert.That(new StandardHttpShieldOptions().Retry.MaxDelay)
            .IsEqualTo(TimeSpan.FromSeconds(10));

    [Test]
    public async Task Standard_Has_The_Documented_Pipeline() =>
        await Assert.That(HttpShield.Standard().ToString()).IsEqualTo(
            "Timeout(30s) → [when HttpRequestException | TaskCanceledException matching predicate | TimeoutExceededException | result predicate] "
            + "Retry(3, exponential 250ms ×2, equal jitter, cap 30s, ≤10s) → CircuitBreaker(50% over 30s, min 10, break 15s) → Timeout(10s)");

    [Test]
    public async Task AttemptTimeout_Greater_Than_TotalTimeout_Throws_At_Build()
    {
        var options = new StandardHttpShieldOptions();
        options.TotalTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(6);

        var exception = await Assert.That(() => HttpShield.Standard(options))
            .Throws<KevlarConfigurationException>();

        await Assert.That(exception!.Message).Contains("AttemptTimeout.Timeout");
        await Assert.That(exception.Message).Contains("TotalTimeout.Timeout");
    }

    [Test]
    public async Task Infinite_TotalTimeout_Disables_Total_Timeout()
    {
        var options = new StandardHttpShieldOptions();
        options.TotalTimeout.Timeout = Timeout.InfiniteTimeSpan;

        await Assert.That(HttpShield.Standard(options).ToString()).IsEqualTo(
            "[when HttpRequestException | TaskCanceledException matching predicate | TimeoutExceededException | result predicate] "
            + "Retry(3, exponential 250ms ×2, equal jitter, cap 30s, ≤10s) → CircuitBreaker(50% over 30s, min 10, break 15s) → Timeout(10s)");
    }

    [Test]
    public async Task Infinite_AttemptTimeout_Disables_Per_Attempt_Timeout()
    {
        var options = new StandardHttpShieldOptions();
        options.AttemptTimeout.Timeout = Timeout.InfiniteTimeSpan;

        await Assert.That(HttpShield.Standard(options).ToString()).IsEqualTo(
            "Timeout(30s) → [when HttpRequestException | TaskCanceledException matching predicate | TimeoutExceededException | result predicate] "
            + "Retry(3, exponential 250ms ×2, equal jitter, cap 30s, ≤10s) → CircuitBreaker(50% over 30s, min 10, break 15s)");
    }

    [Test]
    [Arguments(true, 0)]
    [Arguments(true, -2)]
    [Arguments(false, 0)]
    [Arguments(false, -2)]
    public async Task Zero_Or_Negative_Timeouts_Throw_With_Property_Name(
        bool total,
        long ticks)
    {
        var options = new StandardHttpShieldOptions();
        var propertyName = total ? "TotalTimeout.Timeout" : "AttemptTimeout.Timeout";
        if (total)
        {
            options.TotalTimeout.Timeout = TimeSpan.FromTicks(ticks);
        }
        else
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromTicks(ticks);
        }

        var exception = await Assert.That(() => HttpShield.Standard(options))
            .Throws<KevlarConfigurationException>();

        await Assert.That(exception!.Message).Contains(propertyName);
    }

    [Test]
    public async Task Configured_Standard_Has_Custom_Stages()
    {
        var options = new StandardHttpShieldOptions
        {
            TotalTimeout = new TimeoutOptions { Timeout = TimeSpan.FromSeconds(20) },
            Retry = new RetryOptions<HttpResponseMessage>
            {
                MaxRetries = 1,
                Backoff = Backoff.None,
                DelayGenerator = HttpShield.RetryAfter,
            },
            CircuitBreaker = new CircuitBreakerOptions<HttpResponseMessage>
            {
                ConsecutiveFailures = 8,
                BreakDuration = TimeSpan.FromSeconds(5),
            },
            ConcurrencyLimit = new ConcurrencyLimitOptions
            {
                MaxConcurrency = 12,
                QueueLimit = 4,
            },
            AttemptTimeout = new TimeoutOptions { Timeout = TimeSpan.FromSeconds(3) },
        };

        await Assert.That(HttpShield.Standard(options).ToString()).IsEqualTo(
            "Timeout(20s) → [when HttpRequestException | TaskCanceledException matching predicate | TimeoutExceededException | result predicate] "
            + "Retry(1, no delay) → CircuitBreaker(8 consecutive, break 5s) → ConcurrencyLimit(12, queue 4) → Timeout(3s)");
    }

    [Test]
    public async Task Configured_Standard_Copies_Local_Handling_Overrides()
    {
        var retryOptions = new StandardHttpShieldOptions();
        retryOptions.Retry.HandlesException = static _ => false;
        retryOptions.Retry.HandlesResult = static _ => false;
        var retryCalls = 0;
        var retryShield = HttpShield.Standard(retryOptions);

        using var response = await retryShield.ExecuteAsync(_ =>
        {
            retryCalls++;
            return new ValueTask<HttpResponseMessage>(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        await Assert.That(retryCalls).IsEqualTo(1);

        var exceptionCalls = 0;
        await Assert.That(async () => await retryShield.ExecuteAsync<HttpResponseMessage>(_ =>
        {
            exceptionCalls++;
            throw new HttpRequestException("not handled");
        })).Throws<HttpRequestException>();
        await Assert.That(exceptionCalls).IsEqualTo(1);

        var breakerOptions = new StandardHttpShieldOptions();
        breakerOptions.Retry.MaxRetries = 0;
        breakerOptions.CircuitBreaker.ConsecutiveFailures = 1;
        breakerOptions.CircuitBreaker.FailureRatio = null;
        breakerOptions.CircuitBreaker.HandlesException = static _ => false;
        var breakerShield = HttpShield.Standard(breakerOptions);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.That(async () => await breakerShield.ExecuteAsync<HttpResponseMessage>(
                _ => throw new HttpRequestException("not handled")))
                .Throws<HttpRequestException>();
        }
    }

    [Test]
    public async Task Standard_CircuitBreaker_Result_Override_Replaces_Transient_Clause()
    {
        var options = new StandardHttpShieldOptions();
        options.Retry.MaxRetries = 0;
        options.CircuitBreaker.ConsecutiveFailures = 1;
        options.CircuitBreaker.FailureRatio = null;
        options.CircuitBreaker.HandlesResult = static response =>
            response.StatusCode == HttpStatusCode.ServiceUnavailable;
        var shield = HttpShield.Standard(options);
        var calls = 0;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var ignored = await shield.ExecuteAsync(_ =>
            {
                calls++;
                return new ValueTask<HttpResponseMessage>(
                    new HttpResponseMessage(HttpStatusCode.InternalServerError));
            });
        }

        using (await shield.ExecuteAsync(_ =>
               {
                   calls++;
                   return new ValueTask<HttpResponseMessage>(
                       new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
               }))
        {
        }

        await Assert.That(async () => await shield.ExecuteAsync(_ =>
        {
            calls++;
            return new ValueTask<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK));
        })).Throws<CircuitOpenException>();
        await Assert.That(calls).IsEqualTo(3);
    }

    [Test]
    public async Task Configured_Standard_Replays_Buffered_Unsafe_Requests()
    {
        var calls = 0;
        var bodies = new List<string>();
        using var inner = new DelegateHandler(async (request, cancellationToken) =>
        {
            calls++;
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(
                calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var services = new ServiceCollection()
            .AddHttpClient("configured-standard")
            .ConfigurePrimaryHttpMessageHandler(() => inner)
            .AddStandardShield(options =>
            {
                options.Retry.MaxRetries = 1;
                options.Retry.Backoff = Backoff.None;
                options.CircuitBreaker.ConsecutiveFailures = 100;
                options.CircuitBreaker.FailureRatio = null;
                options.Handler.ContentReplayPolicy = HttpContentReplayPolicy.Buffer;
                options.Handler.AllowUnsafeMethodReplay = true;
            })
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("configured-standard");
        using var content = new StringContent("payload");
        using var response = await client.PostAsync("http://localhost/test", content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(bodies).IsEquivalentTo(["payload", "payload"]);
    }

    [Test]
    public async Task Post_Without_OptIn_Returns_Original_Response_Without_Retry_Delay()
    {
        var now = new DateTimeOffset(2035, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var retryCallbacks = 0;
        var shield = HttpShield.WhenTransient()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.Constant(TimeSpan.FromMinutes(1));
                options.OnRetry = _ => retryCallbacks++;
            })
            .WithTimeProvider(timeProvider);
        using var inner = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var client = CreateClient(inner, shield);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/upload")
        {
            Content = new StringContent("payload"),
        };

        using var response = await client.SendAsync(request).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(retryCallbacks).IsEqualTo(0);
        await Assert.That(timeProvider.GetUtcNow()).IsEqualTo(now);
    }

    [Test]
    public async Task Standard_ServiceProvider_Configuration_Runs_Once_Per_Handler_Lifetime()
    {
        var marker = new Marker();
        var configureCalls = 0;
        using var services = new ServiceCollection()
            .AddSingleton(marker)
            .AddHttpClient("configured-standard-factory")
            .ConfigurePrimaryHttpMessageHandler(() => new DelegateHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
            .AddStandardShield((provider, options) =>
            {
                configureCalls++;
                if (!ReferenceEquals(provider.GetRequiredService<Marker>(), marker))
                {
                    throw new InvalidOperationException("Unexpected service provider.");
                }

                options.Retry.MaxRetries = 0;
            })
            .Services
            .BuildServiceProvider();
        var factory = services.GetRequiredService<IHttpClientFactory>();

        using var firstClient = factory.CreateClient("configured-standard-factory");
        using var secondClient = factory.CreateClient("configured-standard-factory");
        using var first = await firstClient.GetAsync("http://localhost/first");
        using var second = await secondClient.GetAsync("http://localhost/second");

        await Assert.That(configureCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Configured_Standard_Enforces_Optional_Concurrency_Limit()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var services = new ServiceCollection()
            .AddHttpClient("limited-standard")
            .ConfigurePrimaryHttpMessageHandler(() => new DelegateHandler(async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }))
            .AddStandardShield(options =>
            {
                options.Retry.MaxRetries = 0;
                options.ConcurrencyLimit = new ConcurrencyLimitOptions
                {
                    MaxConcurrency = 1,
                    QueueLimit = 0,
                };
            })
            .Services
            .BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("limited-standard");
        var first = client.GetAsync("http://localhost/first");
        await entered.Task;

        await Assert.That(async () => await client.GetAsync("http://localhost/second"))
            .Throws<ConcurrencyLimitExceededException>();

        release.SetResult();
        using var response = await first;
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Standard_Configuration_Rejects_Invalid_Options_Immediately()
    {
        var builder = new ServiceCollection().AddHttpClient("invalid-standard");

        var exception = await Assert.That(() => builder.AddStandardShield(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.Zero;
        })).Throws<KevlarConfigurationException>();

        await Assert.That(exception!.Message).Contains("AttemptTimeout.Timeout");
    }

    [Test]
    public async Task Standard_Disposes_Superseded_Responses_But_Not_The_Winner()
    {
        var timeProvider = new RetrySignalingFakeTimeProvider();
        var contents = Enumerable.Range(0, 3).Select(_ => new TrackingContent()).ToArray();
        var calls = 0;
        using var inner = new DelegateHandler((_, _) =>
        {
            var index = calls++;
            return Task.FromResult(new HttpResponseMessage(
                index < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = contents[index],
            });
        });
        using var client = CreateClient(inner, HttpShield.Standard().WithTimeProvider(timeProvider));

        var nextRetryTimer = timeProvider.NextRetryTimer;
        var task = client.GetAsync("http://localhost/test");
        await AdvanceUntilCompleted(task, timeProvider, nextRetryTimer);
        var response = await task;

        await Assert.That(calls).IsEqualTo(3);
        await Assert.That(contents[0].IsDisposed).IsTrue();
        await Assert.That(contents[1].IsDisposed).IsTrue();
        await Assert.That(contents[2].IsDisposed).IsFalse();

        response.Dispose();
        await Assert.That(contents[2].IsDisposed).IsTrue();
    }

    [Test]
    public async Task Standard_Direct_Execution_Disposes_Superseded_Responses()
    {
        var timeProvider = new RetrySignalingFakeTimeProvider();
        var contents = Enumerable.Range(0, 3).Select(_ => new TrackingContent()).ToArray();
        var calls = 0;
        var shield = HttpShield.Standard().WithTimeProvider(timeProvider);

        var nextRetryTimer = timeProvider.NextRetryTimer;
        var task = shield.ExecuteAsync(_ =>
        {
            var index = calls++;
            return new ValueTask<HttpResponseMessage>(new HttpResponseMessage(
                index < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = contents[index],
            });
        }).AsTask();
        await AdvanceUntilCompleted(task, timeProvider, nextRetryTimer);
        using var response = await task;

        await Assert.That(contents[0].IsDisposed).IsTrue();
        await Assert.That(contents[1].IsDisposed).IsTrue();
        await Assert.That(contents[2].IsDisposed).IsFalse();
    }

    [Test]
    public async Task Standard_Returns_And_Leaves_Final_Transient_Response_Caller_Owned()
    {
        var timeProvider = new RetrySignalingFakeTimeProvider();
        var contents = Enumerable.Range(0, 4).Select(_ => new TrackingContent()).ToArray();
        var calls = 0;
        using var inner = new DelegateHandler((_, _) =>
        {
            var content = contents[calls++];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = content });
        });
        using var client = CreateClient(inner, HttpShield.Standard().WithTimeProvider(timeProvider));

        var nextRetryTimer = timeProvider.NextRetryTimer;
        var task = client.GetAsync("http://localhost/test");
        await AdvanceUntilCompleted(task, timeProvider, nextRetryTimer);
        var response = await task;

        await Assert.That(calls).IsEqualTo(4);
        await Assert.That(contents.Take(3).All(content => content.IsDisposed)).IsTrue();
        await Assert.That(contents[3].IsDisposed).IsFalse();

        response.Dispose();
        await Assert.That(contents[3].IsDisposed).IsTrue();
    }

    [Test]
    public async Task Standard_Attempt_Timeout_Cancels_Inner_And_Retries()
    {
        var timeProvider = new ControlledTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var inner = new DelegateHandler(async (_, token) =>
        {
            var attempt = Interlocked.Increment(ref calls);
            if (attempt == 1)
            {
                firstStarted.SetResult();
                token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), firstCancelled);
            }
            else if (attempt == 2)
            {
                secondStarted.SetResult();
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = CreateClient(inner, HttpShield.Standard().WithTimeProvider(timeProvider));

        var task = client.GetAsync("http://localhost/test", cancellation.Token);
        await firstStarted.Task;
        await timeProvider.WaitForTimersAsync(2);
        timeProvider.FireTimer(1);
        await firstCancelled.Task;
        await timeProvider.WaitForTimersAsync(3);
        timeProvider.FireTimer(2);
        await timeProvider.WaitForTimersAsync(4);
        await secondStarted.Task;

        cancellation.Cancel();
        await Assert.That(async () => await task).Throws<OperationCanceledException>();
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Standard_Opens_Breaker_After_Ten_Transient_Attempts()
    {
        var timeProvider = new RetrySignalingFakeTimeProvider();
        var calls = 0;
        using var inner = new DelegateHandler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        using var client = CreateClient(inner, HttpShield.Standard().WithTimeProvider(timeProvider));

        for (var request = 0; request < 2; request++)
        {
            var nextRetryTimer = timeProvider.NextRetryTimer;
            var task = client.GetAsync($"http://localhost/{request}");
            await AdvanceUntilCompleted(task, timeProvider, nextRetryTimer);
            using var response = await task;
        }

        var openingRetryTimer = timeProvider.NextRetryTimer;
        var openingRequest = client.GetAsync("http://localhost/open");
        await AdvanceUntilCompleted(openingRequest, timeProvider, openingRetryTimer);
        await Assert.That(async () => await openingRequest).Throws<CircuitOpenException>();
        await Assert.That(calls).IsEqualTo(10);

        await Assert.That(async () => await client.GetAsync("http://localhost/rejected"))
            .Throws<CircuitOpenException>();
        await Assert.That(calls).IsEqualTo(10);
    }

    [Test]
    public async Task Caller_Cancellation_Reaches_Inner_And_Stops_Retries()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken observedToken = default;
        var calls = 0;
        using var inner = new DelegateHandler(async (_, token) =>
        {
            calls++;
            observedToken = token;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = CreateClient(inner, HttpShield.WhenTransient().Retry(3, Backoff.None));

        var task = client.GetAsync("http://localhost/test", cancellation.Token);
        cancellation.Cancel();

        await Assert.That(async () => await task).Throws<OperationCanceledException>();
        await Assert.That(observedToken.CanBeCanceled).IsTrue();
        await Assert.That(observedToken.IsCancellationRequested).IsTrue();
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Retry_Preserves_Request_Metadata_And_Buffered_Content()
    {
        var snapshots = new List<RequestSnapshot>();
        var optionKey = new HttpRequestOptionsKey<string>("contract-key");
        using var inner = new DelegateHandler(async (request, token) =>
        {
            using var destination = new MemoryStream();
            await request.Content!.CopyToAsync(destination, token);
            _ = request.Options.TryGetValue(optionKey, out var option);
            snapshots.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri,
                request.Headers.GetValues("X-Contract").Single(),
                option!,
                destination.ToArray()));

            return new HttpResponseMessage(
                snapshots.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var client = CreateClient(
            inner,
            HttpShield.WhenTransient().Retry(1, Backoff.None),
            new ShieldHttpHandlerOptions
            {
                ContentReplayPolicy = HttpContentReplayPolicy.Buffer,
                AllowUnsafeMethodReplay = true,
            });
        using var request = new HttpRequestMessage(HttpMethod.Patch, "http://localhost/items/42?mode=fast")
        {
            Content = new ByteArrayContent([1, 2, 3, 4]),
        };
        request.Headers.Add("X-Contract", "preserved");
        request.Options.Set(optionKey, "option-value");

        using var response = await client.SendAsync(request);

        await Assert.That(snapshots.Count).IsEqualTo(2);
        await Assert.That(snapshots[1].Method).IsEqualTo(snapshots[0].Method);
        await Assert.That(snapshots[1].Uri).IsEqualTo(snapshots[0].Uri);
        await Assert.That(snapshots[1].Header).IsEqualTo(snapshots[0].Header);
        await Assert.That(snapshots[1].Option).IsEqualTo(snapshots[0].Option);
        await Assert.That(snapshots[1].Body).IsEquivalentTo(snapshots[0].Body);
    }

    [Test]
    public async Task Retry_Returns_First_Response_For_OneShot_Stream_Content()
    {
        var bodies = new List<byte[]>();
        using var inner = new DelegateHandler(async (request, token) =>
        {
            using var destination = new MemoryStream();
            await request.Content!.CopyToAsync(destination, token);
            bodies.Add(destination.ToArray());
            return new HttpResponseMessage(
                bodies.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var client = CreateClient(inner, HttpShield.WhenTransient().Retry(1, Backoff.None));
        using var source = new NonSeekableStream([1, 2, 3]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/upload")
        {
            Content = new StreamContent(source),
        };

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(bodies[0]).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(bodies.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Shared_Handler_Keeps_Parallel_Retries_Isolated()
    {
        var attempts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        using var inner = new DelegateHandler((request, _) =>
        {
            var key = request.RequestUri!.AbsolutePath;
            var attempt = attempts.AddOrUpdate(key, 1, static (_, current) => current + 1);
            return Task.FromResult(new HttpResponseMessage(
                attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        });
        using var client = CreateClient(inner, HttpShield.WhenTransient().Retry(1, Backoff.None));

        var tasks = Enumerable.Range(0, 32)
            .Select(index => client.GetAsync($"http://localhost/{index}"))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        try
        {
            await Assert.That(responses.All(response => response.StatusCode == HttpStatusCode.OK)).IsTrue();
            await Assert.That(attempts.Count).IsEqualTo(32);
            await Assert.That(attempts.Values.All(attempt => attempt == 2)).IsTrue();
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task AddShield_Instance_Uses_The_Given_Shield()
    {
        var calls = 0;
        using var services = new ServiceCollection()
            .AddHttpClient("fixed")
            .ConfigurePrimaryHttpMessageHandler(() => new DelegateHandler((_, _) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(
                    calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
            }))
            .AddShield(HttpShield.WhenTransient().Retry(1, Backoff.None))
            .Services
            .BuildServiceProvider();

        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("fixed");
        using var response = await client.GetAsync("http://localhost/test");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task AddShield_Factory_Runs_Once_Per_Handler_Lifetime_With_ServiceProvider()
    {
        var marker = new Marker();
        var factoryCalls = 0;
        using var services = new ServiceCollection()
            .AddSingleton(marker)
            .AddHttpClient("factory")
            .ConfigurePrimaryHttpMessageHandler(() => new DelegateHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
            .AddShield(provider =>
            {
                factoryCalls++;
                if (!ReferenceEquals(provider.GetRequiredService<Marker>(), marker))
                {
                    throw new InvalidOperationException("Unexpected service provider.");
                }

                return HttpShield.WhenTransient().Retry(1, Backoff.None);
            })
            .Services
            .BuildServiceProvider();
        var factory = services.GetRequiredService<IHttpClientFactory>();

        using var firstClient = factory.CreateClient("factory");
        using var secondClient = factory.CreateClient("factory");
        using var first = await firstClient.GetAsync("http://localhost/first");
        using var second = await secondClient.GetAsync("http://localhost/second");

        await Assert.That(factoryCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Handler_Disposal_Disposes_Inner_Handler()
    {
        var inner = new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = CreateClient(inner, Shield<HttpResponseMessage>.Empty);

        client.Dispose();

        await Assert.That(inner.IsDisposed).IsTrue();
    }

    [Test]
    public async Task Http_Registration_Null_Guards_Are_Immediate()
    {
        IHttpClientBuilder? nullBuilder = null;
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("guards");

        await Assert.That(() => HttpShield.Standard(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new ShieldDelegatingHandler(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => ShieldHttpClientBuilderExtensions.AddShield(nullBuilder!, Shield<HttpResponseMessage>.Empty))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield((Shield<HttpResponseMessage>)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield(
                Shield<HttpResponseMessage>.Empty,
                null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield((Func<IServiceProvider, Shield<HttpResponseMessage>>)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddShield(
                static _ => Shield<HttpResponseMessage>.Empty,
                null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new ShieldDelegatingHandler(
                Shield<HttpResponseMessage>.Empty,
                null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ShieldHttpClientBuilderExtensions.AddStandardShield(nullBuilder!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddStandardShield((Action<StandardHttpShieldOptions>)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddStandardShield(
                (Action<IServiceProvider, StandardHttpShieldOptions>)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ShieldHttpClientBuilderExtensions.AddStandardHedgeShield(
                nullBuilder!, _ => { }))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddStandardHedgeShield(null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => builder.AddStandardHedgeShield(_ => { }))
            .Throws<ArgumentException>();
    }

    private static HttpClient CreateClient(HttpMessageHandler inner, Shield<HttpResponseMessage> shield) =>
        new(new ShieldDelegatingHandler(shield) { InnerHandler = inner });

    private static HttpClient CreateClient(
        HttpMessageHandler inner,
        Shield<HttpResponseMessage> shield,
        ShieldHttpHandlerOptions options) =>
        new(new ShieldDelegatingHandler(shield, options) { InnerHandler = inner });

    private static RetryConditionHeaderValue? CreateRetryAfter(RetryAfterKind kind, DateTimeOffset now) => kind switch
    {
        RetryAfterKind.Absent => null,
        RetryAfterKind.ShorterDelta => new RetryConditionHeaderValue(TimeSpan.FromSeconds(2)),
        RetryAfterKind.EqualDelta => new RetryConditionHeaderValue(TimeSpan.FromSeconds(3)),
        RetryAfterKind.LongerDelta => new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)),
        RetryAfterKind.PastDate => new RetryConditionHeaderValue(now - TimeSpan.FromSeconds(1)),
        RetryAfterKind.CurrentDate => new RetryConditionHeaderValue(now),
        RetryAfterKind.LongerDate => new RetryConditionHeaderValue(now + TimeSpan.FromSeconds(7)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static async Task<TimeSpan> ObserveRetryDelay(
        FakeTimeProvider timeProvider,
        Func<HttpResponseMessage> firstAttempt,
        Func<RetryEvent<HttpResponseMessage>, TimeSpan?>? delayGenerator = null,
        TimeSpan? backoff = null)
    {
        TimeSpan? observed = null;
        var calls = 0;
        var shield = HttpShield.WhenTransient()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.Constant(backoff ?? TimeSpan.FromSeconds(3));
                options.DelayGenerator = delayGenerator ?? HttpShield.RetryAfter;
                options.OnRetry = retry => observed = retry.Delay;
            })
            .WithTimeProvider(timeProvider);

        var task = shield.ExecuteAsync(_ => new ValueTask<HttpResponseMessage>(
            ++calls == 1 ? firstAttempt() : new HttpResponseMessage(HttpStatusCode.OK))).AsTask();
        await WaitUntilAsync(() => observed.HasValue);
        timeProvider.Advance(observed!.Value);
        using var response = await task;
        return observed.Value;
    }

    private static async Task<TimeSpan> ObserveStandardRetryDelay(
        StandardHttpShieldOptions options,
        TimeSpan retryAfter)
    {
        var timeProvider = new FakeTimeProvider();
        var calls = 0;
        TimeSpan? observed = null;
        options.Retry.OnRetry = retry => observed = retry.Delay;
        var shield = HttpShield.Standard(options).WithTimeProvider(timeProvider);
        var task = shield.ExecuteAsync(_ =>
        {
            var response = new HttpResponseMessage(
                ++calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (calls == 1)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
            }

            return new ValueTask<HttpResponseMessage>(response);
        }).AsTask();

        await WaitUntilAsync(() => observed.HasValue);
        timeProvider.Advance(observed!.Value);
        using var response = await task;
        return observed.Value;
    }

    private static async Task AdvanceUntilCompleted(
        Task task,
        RetrySignalingFakeTimeProvider timeProvider,
        int nextRetryTimer)
    {
        for (var step = 0; step < 5 && !task.IsCompleted; step++)
        {
            var timerRegistered = timeProvider.WaitForRetryTimersAsync(nextRetryTimer++);
            if (await Task.WhenAny(task, timerRegistered) == task)
            {
                break;
            }

            timeProvider.Advance(TimeSpan.FromSeconds(3));
        }

        if (!task.IsCompleted)
        {
            throw new TimeoutException("Fake-time execution did not complete.");
        }
    }

    private sealed class RetrySignalingFakeTimeProvider : FakeTimeProvider
    {
        private readonly AsyncCounter _retryTimers = new("HTTP retry timers");

        public int NextRetryTimer => _retryTimers.Count + 1;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime < TimeSpan.FromSeconds(5))
            {
                _retryTimers.Signal();
            }

            return timer;
        }

        public Task<int> WaitForRetryTimersAsync(int count) => _retryTimers.WaitForAsync(count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Yield();
        }

        if (!condition())
        {
            throw new TimeoutException("Condition was not reached.");
        }
    }

    public enum RetryAfterKind
    {
        Absent,
        ShorterDelta,
        EqualDelta,
        LongerDelta,
        PastDate,
        CurrentDate,
        LongerDate,
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public bool IsDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingContent : ByteArrayContent
    {
        public TrackingContent()
            : base([])
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();

        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }
    }

    private sealed class Marker;

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri? Uri,
        string Header,
        string Option,
        byte[] Body);
}

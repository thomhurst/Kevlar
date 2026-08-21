using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Grpc;
using Kevlar.IntegrationTests.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kevlar.IntegrationTests;

[NotInParallel]
public class GrpcResilienceTests
{
    [Test]
    public async Task Unary_Success_Preserves_Response_And_Metadata()
    {
        await using var server = await GrpcTestServer.StartAsync();
        using var call = server.Client(Shield.Empty).UnaryAsync(new TestRequest { Scenario = "success" });

        var response = await call.ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(1);
        await Assert.That((await call.ResponseHeadersAsync).GetValue("attempt")).IsEqualTo("1");
        await Assert.That(call.GetStatus().StatusCode).IsEqualTo(StatusCode.OK);
        await Assert.That(call.GetTrailers().GetValue("completed")).IsEqualTo("true");
    }

    [Test]
    public async Task Single_Attempt_Forwards_Headers_Before_Response_Completes()
    {
        await using var server = await GrpcTestServer.StartAsync();
        using var call = server.Client(Shield.Empty).UnaryAsync(
            new TestRequest { Scenario = "headers_wait" });

        var headers = await call.ResponseHeadersAsync.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(headers.GetValue("attempt")).IsEqualTo("1");

        server.State.Release();
        var response = await call.ResponseAsync.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(response.Attempt).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Configured_Single_Attempt_Forwards_Headers_Before_Response_Completes(bool hedge)
    {
        await using var server = await GrpcTestServer.StartAsync();
        var shield = hedge
            ? GrpcShield.WhenTransient().Hedge(1, TimeSpan.Zero)
            : GrpcShield.WhenTransient().Retry(0, Backoff.None);
        using var call = server.Client(shield).UnaryAsync(
            new TestRequest { Scenario = "headers_wait" });

        var headers = await call.ResponseHeadersAsync.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(headers.GetValue("attempt")).IsEqualTo("1");

        server.State.Release();
        var response = await call.ResponseAsync.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(response.Attempt).IsEqualTo(1);
    }

    [Test]
    public async Task Transient_Helper_Retries_Only_Opted_In_Statuses()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var shield = GrpcShield.WhenTransient().Retry(1, Backoff.None);

        using var successfulCall = server.Client(shield).UnaryAsync(
            new TestRequest { Scenario = "transient" });
        var response = await successfulCall.ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(2);

        using var failedCall = server.Client(shield).UnaryAsync(
            new TestRequest { Scenario = "invalid" });
        var exception = await Assert.That(async () => await failedCall.ResponseAsync)
            .Throws<RpcException>();
        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
        await Assert.That(server.State.Attempts("invalid")).IsEqualTo(1);
    }

    [Test]
    public async Task Circuit_Rejection_Does_Not_Invoke_The_Server()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var shield = GrpcShield.WhenTransient()
            .CircuitBreaker(1, TimeSpan.FromMinutes(1));
        var client = server.Client(shield);

        using var failedCall = client.UnaryAsync(new TestRequest { Scenario = "unavailable" });
        _ = await Assert.That(async () => await failedCall.ResponseAsync)
            .Throws<RpcException>();
        using var rejectedCall = client.UnaryAsync(new TestRequest { Scenario = "unavailable" });
        _ = await Assert.That(async () => await rejectedCall.ResponseAsync)
            .Throws<CircuitOpenException>();
        _ = await Assert.That(async () => await rejectedCall.ResponseHeadersAsync)
            .Throws<CircuitOpenException>();
        _ = await Assert.That(() => rejectedCall.GetStatus()).Throws<InvalidOperationException>();
        _ = await Assert.That(() => rejectedCall.GetTrailers()).Throws<InvalidOperationException>();

        await Assert.That(server.State.Attempts("unavailable")).IsEqualTo(1);
    }

    [Test]
    public async Task Kevlar_Timeout_Cancels_The_Underlying_Call()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var client = server.Client(Shield.Timeout(TimeSpan.FromSeconds(1)));

        using var call = client.UnaryAsync(new TestRequest { Scenario = "wait" });
        await server.State.WaitForEntryAsync().WaitAsync(TimeSpan.FromSeconds(5));
        _ = await Assert.That(async () => await call.ResponseAsync)
            .Throws<TimeoutExceededException>();

        await server.State.WaitForCancellationAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Caller_Cancellation_Reaches_The_Underlying_Call()
    {
        await using var server = await GrpcTestServer.StartAsync();
        using var cancellation = new CancellationTokenSource();
        using var call = server.Client(Shield.Empty).UnaryAsync(
            new TestRequest { Scenario = "wait" },
            cancellationToken: cancellation.Token);
        await server.State.WaitForEntryAsync().WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        var exception = await Assert.That(async () => await call.ResponseAsync)
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await server.State.WaitForCancellationAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Grpc_Deadline_Remains_An_RpcException_When_It_Expires_First()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var client = server.Client(Shield.Timeout(TimeSpan.FromSeconds(5)));

        using var call = client.UnaryAsync(
            new TestRequest { Scenario = "wait" },
            deadline: DateTime.UtcNow.AddSeconds(1));
        await server.State.WaitForEntryAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var exception = await Assert.That(async () => await call.ResponseAsync)
            .Throws<RpcException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.DeadlineExceeded);
        await server.State.WaitForCancellationAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Expired_Grpc_Deadline_Stops_Retry_Forever()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var client = server.Client(GrpcShield.WhenTransient().RetryForever(Backoff.None));

        using var call = client.UnaryAsync(
            new TestRequest { Scenario = "wait" },
            deadline: DateTime.UtcNow.AddMilliseconds(250));
        var exception = await Assert.That(async () =>
                await call.ResponseAsync.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<RpcException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.DeadlineExceeded);
        await Assert.That(server.State.Attempts("wait")).IsEqualTo(1);
    }

    [Test]
    public async Task Expired_Grpc_Deadline_Stops_Unconditional_Retry_Forever()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var shield = Shield.When(static _ => true).RetryForever(Backoff.None);
        var client = server.Client(shield);

        using var call = client.UnaryAsync(
            new TestRequest { Scenario = "wait" },
            deadline: DateTime.UtcNow.AddMilliseconds(250));
        var exception = await Assert.That(async () =>
                await call.ResponseAsync.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<RpcException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.DeadlineExceeded);
        await Assert.That(server.State.Attempts("wait")).IsEqualTo(1);
    }

    [Test]
    public async Task Deadline_Admission_Cutoff_Preserves_The_Failed_Attempt_Metadata()
    {
        var response = new TaskCompletionSource<TestReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var trailers = new Metadata { { "selected", "true" } };
        var status = new Status(StatusCode.DeadlineExceeded, "deadline");
        var expected = new RpcException(status, trailers);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var invoker = new DelegateCallInvoker((_, _) =>
        {
            Interlocked.Increment(ref attempts);
            started.TrySetResult();
            return Call(
                response.Task,
                status: status,
                headers: new Metadata { { "attempt", "1" } },
                trailers: trailers);
        }).Intercept(new ShieldUnaryClientInterceptor(
            Shield.When(static _ => true).RetryForever(Backoff.None)));
        var client = new Resilience.ResilienceClient(invoker);
        using var call = client.UnaryAsync(
            new TestRequest(),
            deadline: DateTime.UtcNow.AddMilliseconds(50));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        response.TrySetException(expected);
        var actual = await Assert.That(async () =>
                await call.ResponseAsync.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<RpcException>();

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That((await call.ResponseHeadersAsync).GetValue("attempt")).IsEqualTo("1");
        await Assert.That(call.GetStatus()).IsEqualTo(status);
        await Assert.That(call.GetTrailers().GetValue("selected")).IsEqualTo("true");
    }

    [Test]
    public async Task Short_Circuit_Does_Not_Reuse_Earlier_Attempt_Metadata()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var shield = GrpcShield.WhenTransient()
            .Retry(1, Backoff.None)
            .CircuitBreaker(1, TimeSpan.FromMinutes(1));
        using var call = server.Client(shield).UnaryAsync(
            new TestRequest { Scenario = "unavailable" });

        _ = await Assert.That(async () => await call.ResponseAsync)
            .Throws<CircuitOpenException>();
        _ = await Assert.That(async () => await call.ResponseHeadersAsync)
            .Throws<CircuitOpenException>();
        _ = await Assert.That(() => call.GetStatus()).Throws<InvalidOperationException>();
        _ = await Assert.That(() => call.GetTrailers()).Throws<InvalidOperationException>();
        await Assert.That(server.State.Attempts("unavailable")).IsEqualTo(1);
    }

    [Test]
    public async Task Kevlar_Timeout_Preserves_Underlying_Terminal_Metadata()
    {
        var response = new TaskCompletionSource<TestReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var status = new Status(StatusCode.Cancelled, "timed out");
        var trailers = new Metadata { { "terminal", "true" } };
        var invoker = new DelegateCallInvoker((_, options) =>
        {
            options.CancellationToken.Register(() =>
                response.TrySetException(new RpcException(status, trailers)));
            return Call(response.Task, status: status, trailers: trailers);
        }).Intercept(new ShieldUnaryClientInterceptor(Shield.Timeout(TimeSpan.FromMilliseconds(50))));
        var client = new Resilience.ResilienceClient(invoker);
        using var call = client.UnaryAsync(new TestRequest());

        _ = await Assert.That(async () => await call.ResponseAsync).Throws<TimeoutExceededException>();

        await Assert.That(call.GetStatus()).IsEqualTo(status);
        await Assert.That(call.GetTrailers().GetValue("terminal")).IsEqualTo("true");
    }

    [Test]
    public async Task Transient_Status_Set_Is_Explicit()
    {
        await Assert.That(GrpcShield.IsTransient(StatusCode.Unavailable)).IsTrue();
        await Assert.That(GrpcShield.IsTransient(StatusCode.DeadlineExceeded)).IsTrue();
        await Assert.That(GrpcShield.IsTransient(StatusCode.ResourceExhausted)).IsTrue();
        await Assert.That(GrpcShield.IsTransient(StatusCode.InvalidArgument)).IsFalse();
        await Assert.That(GrpcShield.IsTransient((RpcException?)null)).IsFalse();
    }

    [Test]
    public async Task Interceptor_Rejects_Null_Shield()
    {
        _ = await Assert.That(() => new ShieldUnaryClientInterceptor(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Hedge_Cancels_And_Disposes_The_Losing_Call()
    {
        var firstResponse = new TaskCompletionSource<TestReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var interceptor = new ShieldUnaryClientInterceptor(Shield.Hedge(2, TimeSpan.Zero));
        var invoker = new DelegateCallInvoker((_, options) =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                options.CancellationToken.Register(() =>
                    firstResponse.TrySetException(new RpcException(new Status(StatusCode.Cancelled, "cancelled"))));
                return Call(firstResponse.Task, () => firstDisposed.TrySetResult());
            }

            return Call(Task.FromResult(new TestReply { Attempt = attempt }));
        }).Intercept(interceptor);
        var client = new Resilience.ResilienceClient(invoker);

        using var call = client.UnaryAsync(new TestRequest());
        var response = await call.ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(2);
        await firstDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Disposing_The_Wrapper_Cancels_And_Disposes_The_Active_Call()
    {
        var response = new TaskCompletionSource<TestReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = 0;
        var invoker = new DelegateCallInvoker((_, options) =>
        {
            started.TrySetResult();
            return Call(response.Task, () =>
            {
                Interlocked.Increment(ref disposed);
                response.TrySetException(new RpcException(new Status(StatusCode.Cancelled, "cancelled")));
            });
        }).Intercept(new ShieldUnaryClientInterceptor(Shield.Empty));
        var client = new Resilience.ResilienceClient(invoker);
        var call = client.UnaryAsync(new TestRequest());

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        call.Dispose();
        call.Dispose();

        _ = await Assert.That(async () =>
                await call.ResponseAsync.WaitAsync(TimeSpan.FromSeconds(5)))
            .Throws<OperationCanceledException>();
        await Assert.That(disposed).IsEqualTo(1);
    }

    [Test]
    public async Task Hedge_Preserves_Metadata_For_Selected_Early_Failure()
    {
        var pending = new TaskCompletionSource<TestReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var losingCallDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var expected = new RpcException(
            new Status(StatusCode.InvalidArgument, "invalid"),
            new Metadata { { "selected", "true" } });
        var invoker = new DelegateCallInvoker((_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                return Call(
                    Task.FromException<TestReply>(expected),
                    status: expected.Status,
                    headers: new Metadata { { "attempt", "1" } },
                    trailers: new Metadata { { "selected", "true" } });
            }

            return Call(pending.Task, () =>
            {
                losingCallDisposed.TrySetResult();
                pending.TrySetException(new RpcException(new Status(StatusCode.Cancelled, "cancelled")));
            });
        }).Intercept(new ShieldUnaryClientInterceptor(
            GrpcShield.WhenTransient().Hedge(2, TimeSpan.Zero)));
        var client = new Resilience.ResilienceClient(invoker);
        using var call = client.UnaryAsync(new TestRequest());

        var actual = await Assert.That(async () => await call.ResponseAsync).Throws<RpcException>();

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That((await call.ResponseHeadersAsync).GetValue("attempt")).IsEqualTo("1");
        await Assert.That(call.GetStatus()).IsEqualTo(expected.Status);
        await Assert.That(call.GetTrailers().GetValue("selected")).IsEqualTo("true");
        await losingCallDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Discarded_Loopback_Failure_Preserves_Response_Headers()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var client = server.Client(Shield.Use(new ReturnFirstAfterSecondStrategy()));
        using var call = client.UnaryAsync(new TestRequest { Scenario = "invalid" });

        var exception = await Assert.That(async () => await call.ResponseAsync).Throws<RpcException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
        await Assert.That((await call.ResponseHeadersAsync).GetValue("attempt")).IsEqualTo("1");
        await Assert.That(server.State.Attempts("invalid")).IsEqualTo(2);
    }

    [Test]
    public async Task Hedge_Selects_Metadata_When_Attempts_Reuse_An_Exception()
    {
        var responses = new[]
        {
            new TaskCompletionSource<TestReply>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<TestReply>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFailureObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var expected = new InvalidOperationException("shared");
        var invoker = new DelegateCallInvoker((_, _) =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 2)
            {
                bothStarted.TrySetResult();
            }

            return Call(
                responses[attempt - 1].Task,
                status: new Status(
                    attempt == 1 ? StatusCode.InvalidArgument : StatusCode.Unavailable,
                    $"attempt {attempt}"),
                headers: new Metadata { { "attempt", attempt.ToString() } },
                trailers: new Metadata { { "selected", attempt.ToString() } });
        }).Intercept(new ShieldUnaryClientInterceptor(
            Shield.Use(new SelectFirstAfterSecondStrategy(secondFailureObserved))));
        var client = new Resilience.ResilienceClient(invoker);
        using var call = client.UnaryAsync(new TestRequest());

        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        responses[1].TrySetException(expected);
        await secondFailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        responses[0].TrySetException(expected);
        var actual = await Assert.That(async () => await call.ResponseAsync).Throws<InvalidOperationException>();

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That((await call.ResponseHeadersAsync).GetValue("attempt")).IsEqualTo("1");
        await Assert.That(call.GetStatus().StatusCode).IsEqualTo(StatusCode.InvalidArgument);
        await Assert.That(call.GetTrailers().GetValue("selected")).IsEqualTo("1");
    }

    [Test]
    public async Task Completed_Call_Detaches_The_Caller_Cancellation_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var cancellationObserved = 0;
        var invoker = new DelegateCallInvoker((_, options) =>
        {
            options.CancellationToken.Register(() => Interlocked.Increment(ref cancellationObserved));
            return Call(Task.FromResult(new TestReply { Attempt = 1 }));
        }).Intercept(new ShieldUnaryClientInterceptor(Shield.Empty));
        var client = new Resilience.ResilienceClient(invoker);
        using var call = client.UnaryAsync(new TestRequest(), cancellationToken: cancellation.Token);

        _ = await call.ResponseAsync;
        cancellation.Cancel();

        await Assert.That(cancellationObserved).IsEqualTo(0);
    }

    [Test]
    public async Task Hedge_Cancels_The_Losing_Loopback_Rpc()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var client = server.Client(Shield.Hedge(2, TimeSpan.Zero));

        using var call = client.UnaryAsync(new TestRequest { Scenario = "hedge" });
        var response = await call.ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(2);
        await server.State.WaitForCancellationAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(server.State.Attempts("hedge")).IsEqualTo(2);
    }

    [Test]
    public async Task Retry_Disposes_Failed_Calls_And_Preserves_Final_RpcException()
    {
        var disposed = 0;
        var attempts = 0;
        var disposedBeforeAttempts = new List<int>();
        var expected = new RpcException(new Status(StatusCode.Unavailable, "final"));
        var interceptor = new ShieldUnaryClientInterceptor(
            GrpcShield.WhenTransient().Retry(1, Backoff.None));
        var invoker = new DelegateCallInvoker((_, _) =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            disposedBeforeAttempts.Add(disposed);
            return Call(
                Task.FromException<TestReply>(attempt == 2
                    ? expected
                    : new RpcException(new Status(StatusCode.Unavailable, "retry"))),
                () => Interlocked.Increment(ref disposed),
                new Status(StatusCode.Unavailable, "final"));
        }).Intercept(interceptor);
        var client = new Resilience.ResilienceClient(invoker);
        using var call = client.UnaryAsync(new TestRequest());

        var actual = await Assert.That(async () => await call.ResponseAsync).Throws<RpcException>();

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(disposed).IsEqualTo(1);
        await Assert.That(disposedBeforeAttempts.SequenceEqual([0, 1])).IsTrue();
        await Assert.That(call.GetStatus().StatusCode).IsEqualTo(StatusCode.Unavailable);
    }

    [Test]
    public async Task Named_DI_Shield_Configures_The_Grpc_Client()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var services = new ServiceCollection();
        services.AddShield("grpc", GrpcShield.WhenTransient().Retry(1, Backoff.None));
        services.AddGrpcClient<Resilience.ResilienceClient>(options =>
            options.Address = new Uri("http://localhost"))
            .ConfigurePrimaryHttpMessageHandler(server.CreateHandler)
            .AddShieldUnaryInterceptor("grpc");
        await using var provider = services.BuildServiceProvider();

        using var call = provider.GetRequiredService<Resilience.ResilienceClient>()
            .UnaryAsync(new TestRequest { Scenario = "transient" });
        var response = await call.ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(2);
    }

    private static AsyncUnaryCall<TestReply> Call(
        Task<TestReply> response,
        Action? dispose = null,
        Status? status = null,
        Metadata? headers = null,
        Metadata? trailers = null) =>
        new(
            response,
            Task.FromResult(headers ?? new Metadata()),
            () => status ?? Status.DefaultSuccess,
            () => trailers ?? new Metadata(),
            dispose ?? NoOp);

    private static void NoOp()
    {
    }

    private sealed class DelegateCallInvoker(
        Func<TestRequest, CallOptions, AsyncUnaryCall<TestReply>> invoke) : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request) =>
            (AsyncUnaryCall<TResponse>)(object)invoke((TestRequest)(object)request, options);

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options) => throw new NotSupportedException();
    }

    private sealed class SelectFirstAfterSecondStrategy(
        TaskCompletionSource secondFailureObserved) : Strategy
    {
        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var first = next.InvokeAsync(context).AsTask();
            var second = next.InvokeAsync(context).AsTask();
            _ = await second.ConfigureAwait(false);
            secondFailureObserved.TrySetResult();
            return await first.ConfigureAwait(false);
        }
    }

    private sealed class ReturnFirstAfterSecondStrategy : Strategy
    {
        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var first = await next.InvokeAsync(context).ConfigureAwait(false);
            _ = await next.InvokeAsync(context).ConfigureAwait(false);
            return first;
        }
    }

    private sealed class GrpcTestServer : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private GrpcTestServer(WebApplication application, GrpcServiceState state, GrpcChannel channel)
        {
            _application = application;
            State = state;
            Channel = channel;
        }

        public GrpcServiceState State { get; }

        private GrpcChannel Channel { get; }

        public static async Task<GrpcTestServer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddGrpc();
            builder.Services.AddSingleton<GrpcServiceState>();
            var application = builder.Build();
            application.MapGrpcService<TestGrpcService>();
            try
            {
                await application.StartAsync();
                var channel = GrpcChannel.ForAddress(
                    "http://localhost",
                    new GrpcChannelOptions { HttpHandler = application.GetTestServer().CreateHandler() });
                return new GrpcTestServer(
                    application,
                    application.Services.GetRequiredService<GrpcServiceState>(),
                    channel);
            }
            catch
            {
                await application.DisposeAsync();
                throw;
            }
        }

        public Resilience.ResilienceClient Client(Shield shield) =>
            new(Channel.Intercept(new ShieldUnaryClientInterceptor(shield)));

        public HttpMessageHandler CreateHandler() => _application.GetTestServer().CreateHandler();

        public async ValueTask DisposeAsync()
        {
            Channel.Dispose();
            await _application.DisposeAsync();
        }
    }

    private sealed class GrpcServiceState
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, int> _attempts = [];
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Record(string scenario)
        {
            lock (_gate)
            {
                _attempts.TryGetValue(scenario, out var attempts);
                return _attempts[scenario] = attempts + 1;
            }
        }

        public int Attempts(string scenario)
        {
            lock (_gate)
            {
                return _attempts.GetValueOrDefault(scenario);
            }
        }

        public Task WaitForEntryAsync() => _entered.Task;

        public Task WaitForCancellationAsync() => _cancelled.Task;

        public void Entered() => _entered.TrySetResult();

        public void Cancelled() => _cancelled.TrySetResult();

        public void Release() => _release.TrySetResult();

        public Task WaitForReleaseAsync() => _release.Task;
    }

    private sealed class TestGrpcService(GrpcServiceState state) : Resilience.ResilienceBase
    {
        public override async Task<TestReply> Unary(TestRequest request, ServerCallContext context)
        {
            var attempt = state.Record(request.Scenario);
            await context.WriteResponseHeadersAsync(new Metadata { { "attempt", attempt.ToString() } });
            context.ResponseTrailers.Add("completed", "true");

            switch (request.Scenario)
            {
                case "transient" when attempt == 1:
                case "unavailable":
                    throw new RpcException(new Status(StatusCode.Unavailable, "transient"));
                case "invalid":
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid"));
                case "wait":
                case "hedge" when attempt == 1:
                    state.Entered();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        state.Cancelled();
                        throw;
                    }

                    break;
                case "headers_wait":
                    await state.WaitForReleaseAsync();
                    break;
            }

            return new TestReply { Attempt = attempt };
        }
    }
}

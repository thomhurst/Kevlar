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
    public async Task Transient_Helper_Retries_Only_Opted_In_Statuses()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var shield = GrpcShield.WhenTransient().Retry(1, Backoff.None);

        var response = await server.Client(shield).UnaryAsync(
            new TestRequest { Scenario = "transient" }).ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(2);

        var exception = await Assert.That(async () =>
                await server.Client(shield).UnaryAsync(
                    new TestRequest { Scenario = "invalid" }).ResponseAsync)
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

        _ = await Assert.That(async () =>
                await client.UnaryAsync(new TestRequest { Scenario = "unavailable" }).ResponseAsync)
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
        var client = server.Client(Shield.Timeout(TimeSpan.FromMilliseconds(100)));

        _ = await Assert.That(async () =>
                await client.UnaryAsync(new TestRequest { Scenario = "wait" }).ResponseAsync)
            .Throws<TimeoutExceededException>();

        await server.State.WaitForCancellationAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Caller_Cancellation_Reaches_The_Underlying_Call()
    {
        await using var server = await GrpcTestServer.StartAsync();
        using var cancellation = new CancellationTokenSource();
        var call = server.Client(Shield.Empty).UnaryAsync(
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

        var exception = await Assert.That(async () =>
                await client.UnaryAsync(
                    new TestRequest { Scenario = "wait" },
                    deadline: DateTime.UtcNow.AddMilliseconds(100)).ResponseAsync)
            .Throws<RpcException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.DeadlineExceeded);
        await server.State.WaitForCancellationAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Transient_Status_Set_Is_Explicit()
    {
        await Assert.That(GrpcShield.IsTransient(StatusCode.Unavailable)).IsTrue();
        await Assert.That(GrpcShield.IsTransient(StatusCode.DeadlineExceeded)).IsTrue();
        await Assert.That(GrpcShield.IsTransient(StatusCode.ResourceExhausted)).IsTrue();
        await Assert.That(GrpcShield.IsTransient(StatusCode.InvalidArgument)).IsFalse();
        await Assert.That(GrpcShield.IsTransient((RpcException)null!)).IsFalse();
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

        var response = await client.UnaryAsync(new TestRequest()).ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(2);
        await firstDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Disposing_The_Wrapper_Cancels_And_Disposes_The_Active_Call()
    {
        var response = new TaskCompletionSource<TestReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = 0;
        var invoker = new DelegateCallInvoker((_, options) =>
        {
            options.CancellationToken.Register(() =>
                response.TrySetException(new RpcException(new Status(StatusCode.Cancelled, "cancelled"))));
            return Call(response.Task, () => Interlocked.Increment(ref disposed));
        }).Intercept(new ShieldUnaryClientInterceptor(Shield.Empty));
        var client = new Resilience.ResilienceClient(invoker);
        var call = client.UnaryAsync(new TestRequest());

        call.Dispose();
        call.Dispose();

        _ = await Assert.That(async () => await call.ResponseAsync).Throws<OperationCanceledException>();
        await Assert.That(disposed).IsEqualTo(1);
    }

    [Test]
    public async Task Hedge_Cancels_The_Losing_Loopback_Rpc()
    {
        await using var server = await GrpcTestServer.StartAsync();
        var client = server.Client(Shield.Hedge(2, TimeSpan.Zero));

        var response = await client.UnaryAsync(
            new TestRequest { Scenario = "hedge" }).ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(2);
        await server.State.WaitForCancellationAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(server.State.Attempts("hedge")).IsEqualTo(2);
    }

    [Test]
    public async Task Retry_Disposes_Failed_Calls_And_Preserves_Final_RpcException()
    {
        var disposed = 0;
        var attempts = 0;
        var expected = new RpcException(new Status(StatusCode.Unavailable, "final"));
        var interceptor = new ShieldUnaryClientInterceptor(
            GrpcShield.WhenTransient().Retry(1, Backoff.None));
        var invoker = new DelegateCallInvoker((_, _) =>
        {
            Interlocked.Increment(ref attempts);
            return Call(
                Task.FromException<TestReply>(expected),
                () => Interlocked.Increment(ref disposed),
                new Status(StatusCode.Unavailable, "final"));
        }).Intercept(interceptor);
        var client = new Resilience.ResilienceClient(invoker);
        using var call = client.UnaryAsync(new TestRequest());

        var actual = await Assert.That(async () => await call.ResponseAsync).Throws<RpcException>();

        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(disposed).IsEqualTo(1);
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

        var response = await provider.GetRequiredService<Resilience.ResilienceClient>()
            .UnaryAsync(new TestRequest { Scenario = "transient" }).ResponseAsync;

        await Assert.That(response.Attempt).IsEqualTo(2);
    }

    private static AsyncUnaryCall<TestReply> Call(
        Task<TestReply> response,
        Action? dispose = null,
        Status? status = null) =>
        new(
            response,
            Task.FromResult(new Metadata()),
            () => status ?? Status.DefaultSuccess,
            static () => new Metadata(),
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
            await application.StartAsync();
            var channel = GrpcChannel.ForAddress(
                "http://localhost",
                new GrpcChannelOptions { HttpHandler = application.GetTestServer().CreateHandler() });
            return new GrpcTestServer(
                application,
                application.Services.GetRequiredService<GrpcServiceState>(),
                channel);
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
            }

            return new TestReply { Attempt = attempt };
        }
    }
}

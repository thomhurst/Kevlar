using Grpc.Core;
using Grpc.Net.Client;
using Kevlar.Extensions.Grpc;
using Kevlar.IntegrationTests.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.IntegrationTests;

[NotInParallel]
public class GrpcServerInterceptorTests
{
    [Test]
    [Arguments("unary")]
    [Arguments("client")]
    [Arguments("server")]
    [Arguments("duplex")]
    public async Task Concurrency_Limit_Covers_The_Whole_Handler_And_Releases_Afterward(string shape)
    {
        await using var server = await Server.StartAsync(services =>
            services.AddShieldServerInterceptor(Shield.ConcurrencyLimit(1, queueLimit: 0)));
        var active = server.CallAsync(shape, "wait");
        await server.State.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var rejection = await Assert.That(async () => await server.CallAsync(shape)).Throws<RpcException>();
        await Assert.That(rejection!.StatusCode).IsEqualTo(StatusCode.ResourceExhausted);
        await Assert.That(server.State.Calls).IsEqualTo(1);
        server.State.Release.TrySetResult();
        await active.WaitAsync(TimeSpan.FromSeconds(5));
        await server.CallAsync(shape);
        await Assert.That(server.State.Calls).IsEqualTo(2);
    }

    [Test]
    [Arguments("unary")]
    [Arguments("client")]
    [Arguments("server")]
    [Arguments("duplex")]
    public async Task Rate_Limit_Rejects_Without_Invoking_Any_Handler_Shape(string shape)
    {
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(RateLimit()));
        await server.CallAsync(shape);
        var rejection = await Assert.That(async () => await server.CallAsync(shape)).Throws<RpcException>();
        await Assert.That(rejection!.StatusCode).IsEqualTo(StatusCode.ResourceExhausted);
        await Assert.That(server.State.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task Circuit_Rejection_Returns_Unavailable_And_Remaining_Break_Duration()
    {
        var time = new FakeTimeProvider();
        var shield = Shield.CircuitBreaker(consecutiveFailures: 1, breakDuration: TimeSpan.FromSeconds(60))
            .WithTimeProvider(time);
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(shield));
        var failure = await Assert.That(async () => await server.CallAsync("unary", "fail")).Throws<RpcException>();
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
        await Assert.That(failure.Trailers.GetValue("original")).IsEqualTo("preserved");
        time.Advance(TimeSpan.FromSeconds(15));
        var rejection = await Assert.That(async () => await server.CallAsync("unary")).Throws<RpcException>();
        await Assert.That(rejection!.StatusCode).IsEqualTo(StatusCode.Unavailable);
        await Assert.That(rejection.Trailers.GetValue("grpc-retry-pushback-ms")).IsEqualTo("45000");
        await Assert.That(server.State.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task Isolated_Circuit_Has_No_Recovery_Estimate()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options => options.Monitor = monitor);
        monitor.Isolate();
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(shield));
        var rejection = await Assert.That(async () => await server.CallAsync("unary")).Throws<RpcException>();
        await Assert.That(rejection!.StatusCode).IsEqualTo(StatusCode.Unavailable);
        await Assert.That(rejection.Trailers.GetValue("grpc-retry-pushback-ms")).IsNull();
        await Assert.That(server.State.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Partitions_Default_To_Method_And_Can_Be_Selected_By_Peer(bool byPeer)
    {
        await using var partitions = new PartitionedShield<string>(_ => RateLimit());
        var peers = new List<string>();
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(
            partitions, byPeer ? context => { peers.Add(context.Peer); return context.Peer; } : null));
        await server.CallAsync("unary");
        if (byPeer)
        {
            var rejection = await Assert.That(async () => await server.CallAsync("server")).Throws<RpcException>();
            await Assert.That(rejection!.StatusCode).IsEqualTo(StatusCode.ResourceExhausted);
            await Assert.That(peers.Count).IsEqualTo(2);
            await Assert.That(peers.Distinct().Count()).IsEqualTo(1);
            await Assert.That(partitions.Count).IsEqualTo(1);
        }
        else
        {
            await server.CallAsync("server");
            await Assert.That(partitions.Count).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Named_Registration_Uses_The_Shared_Registry_Shield()
    {
        await using var server = await Server.StartAsync(services =>
        {
            services.AddShield("server", RateLimit());
            services.AddShieldServerInterceptor("server");
        });
        await server.CallAsync("unary");
        var rejection = await Assert.That(async () => await server.CallAsync("unary")).Throws<RpcException>();
        await Assert.That(rejection!.StatusCode).IsEqualTo(StatusCode.ResourceExhausted);
    }

    [Test]
    public async Task Asynchronous_Partition_Factories_Are_Awaited_And_Reused()
    {
        var created = 0;
        await using var partitions = PartitionedShield<string>.CreateAsync(async _ =>
        {
            await Task.Yield();
            Interlocked.Increment(ref created);
            return RateLimit();
        });
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(partitions));
        await server.CallAsync("unary");
        var rejection = await Assert.That(async () => await server.CallAsync("unary")).Throws<RpcException>();
        await Assert.That(rejection!.StatusCode).IsEqualTo(StatusCode.ResourceExhausted);
        await Assert.That(created).IsEqualTo(1);
    }

    [Test]
    [Arguments(1L, "1")]
    [Arguments(-1L, "0")]
    [Arguments(long.MaxValue, "2147483647")]
    public async Task Pushback_Rounds_Up_And_Clamps_To_Protocol_Integer_Range(long ticks, string expected)
    {
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(
            Shield.Use(new CircuitRejection(TimeSpan.FromTicks(ticks)))));
        var rejection = await Assert.That(async () => await server.CallAsync("unary")).Throws<RpcException>();
        await Assert.That(rejection!.Trailers.GetValue("grpc-retry-pushback-ms")).IsEqualTo(expected);
    }

    [Test]
    public async Task Token_Replacing_Strategies_Are_Rejected_Before_Handler_Execution()
    {
        var time = new FakeTimeProvider();
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(
            Shield.Timeout(TimeSpan.FromSeconds(10)).WithTimeProvider(time)));
        _ = await Assert.That(async () => await server.CallAsync("unary")).Throws<RpcException>();
        await Assert.That(server.State.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task Caller_Cancellation_Releases_The_Concurrency_Permit()
    {
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(
            Shield.ConcurrencyLimit(1, queueLimit: 0)));
        using var cancellation = new CancellationTokenSource();
        var call = server.CallAsync("unary", "cancel", cancellation.Token);
        await server.State.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        _ = await Assert.That(async () => await call).Throws<RpcException>();
        await server.State.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The service's signal precedes shield cleanup; await server request completion as well.
        await server.State.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.CallAsync("unary");
    }

    [Test]
    public async Task Headers_Trailers_And_Context_Properties_Are_Preserved()
    {
        await using var server = await Server.StartAsync(services => services.AddShieldServerInterceptor(
            Shield.ConcurrencyLimit(1, queueLimit: 0)));
        using var call = server.Client.UnaryAsync(new TestRequest());
        await call.ResponseAsync;
        await Assert.That((await call.ResponseHeadersAsync).GetValue("header")).IsEqualTo("preserved");
        await Assert.That(call.GetTrailers().GetValue("trailer")).IsEqualTo("preserved");
        await Assert.That(server.State.HasHttpContext).IsTrue();
    }

    [Test]
    public async Task Multi_Attempt_Shields_Are_Rejected_Before_Server_Execution()
    {
        await Assert.That(() => new ShieldServerInterceptor(Shield.Retry(1))).Throws<ArgumentException>();
        await Assert.That(() => new ShieldServerInterceptor(Shield.Hedge(1, delay: TimeSpan.Zero)))
            .Throws<ArgumentException>();
        await Assert.That(() => new ShieldServerInterceptor((Shield)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new ShieldServerInterceptor((PartitionedShield<string>)null!))
            .Throws<ArgumentNullException>();
    }

    private static Shield RateLimit() => Shield.RateLimit(1, perWindow: TimeSpan.FromMinutes(1));

    private sealed class CircuitRejection(TimeSpan retryAfter) : Strategy
    {
        protected override bool InvokesContinuationAtMostOnce => true;
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context) =>
            new(Outcome<T>.FromException(new CircuitOpenException(retryAfter, isIsolated: false, lastException: null)));
    }

    private sealed class State
    {
        public int Calls;
        public bool HasHttpContext;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Service(State state) : Resilience.ResilienceBase
    {
        private async Task<TestReply> HandleAsync(TestRequest request, ServerCallContext context)
        {
            Interlocked.Increment(ref state.Calls);
            state.HasHttpContext = context.GetHttpContext() is not null;
            if (request.Scenario == "fail")
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "original"),
                    new Metadata { { "original", "preserved" } });
            }
            await context.WriteResponseHeadersAsync(new Metadata { { "header", "preserved" } });
            context.ResponseTrailers.Add("trailer", "preserved");
            state.Entered.TrySetResult();
            try
            {
                if (request.Scenario == "wait") { await state.Release.Task.WaitAsync(context.CancellationToken); }
                if (request.Scenario == "cancel") { await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken); }
            }
            catch (OperationCanceledException)
            {
                state.Cancelled.TrySetResult();
                throw;
            }
            return new TestReply { Attempt = state.Calls };
        }

        public override Task<TestReply> Unary(TestRequest request, ServerCallContext context) => HandleAsync(request, context);

        public override async Task ServerStream(TestRequest request, IServerStreamWriter<TestReply> responseStream, ServerCallContext context) =>
            await responseStream.WriteAsync(await HandleAsync(request, context));

        public override async Task<TestReply> ClientStream(IAsyncStreamReader<TestRequest> requestStream, ServerCallContext context)
        {
            await requestStream.MoveNext(context.CancellationToken);
            return await HandleAsync(requestStream.Current, context);
        }

        public override async Task DuplexStream(IAsyncStreamReader<TestRequest> requestStream, IServerStreamWriter<TestReply> responseStream,
            ServerCallContext context)
        {
            await requestStream.MoveNext(context.CancellationToken);
            await responseStream.WriteAsync(await HandleAsync(requestStream.Current, context));
        }
    }

    private sealed class Server(WebApplication application, GrpcChannel channel, State state) : IAsyncDisposable
    {
        public State State => state;
        public Resilience.ResilienceClient Client { get; } = new(channel);

        public static async Task<Server> StartAsync(Action<IServiceCollection> configure)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddGrpc(options => options.Interceptors.Add<ShieldServerInterceptor>());
            var state = new State();
            builder.Services.AddSingleton(state);
            configure(builder.Services);
            var application = builder.Build();
            application.Use(async (context, next) =>
            {
                try { await next(context); }
                finally { state.Completed.TrySetResult(); }
            });
            application.MapGrpcService<Service>();
            await application.StartAsync();
            var channel = GrpcChannel.ForAddress("http://localhost",
                new GrpcChannelOptions { HttpHandler = application.GetTestServer().CreateHandler() });
            return new Server(application, channel, state);
        }

        public async Task CallAsync(string shape, string scenario = "", CancellationToken cancellationToken = default)
        {
            var request = new TestRequest { Scenario = scenario };
            switch (shape)
            {
                case "unary":
                    using (var call = Client.UnaryAsync(request, cancellationToken: cancellationToken))
                    { await call.ResponseAsync; }
                    break;
                case "server":
                    using (var call = Client.ServerStream(request, cancellationToken: cancellationToken))
                    { while (await call.ResponseStream.MoveNext(cancellationToken)) { } }
                    break;
                case "client":
                    using (var call = Client.ClientStream(cancellationToken: cancellationToken))
                    {
                        await call.RequestStream.WriteAsync(request);
                        await call.RequestStream.CompleteAsync();
                        await call.ResponseAsync;
                    }
                    break;
                case "duplex":
                    using (var call = Client.DuplexStream(cancellationToken: cancellationToken))
                    {
                        await call.RequestStream.WriteAsync(request);
                        await call.RequestStream.CompleteAsync();
                        while (await call.ResponseStream.MoveNext(cancellationToken)) { }
                    }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(shape));
            }
        }

        public async ValueTask DisposeAsync()
        {
            state.Release.TrySetResult();
            channel.Dispose();
            await application.DisposeAsync();
        }
    }
}

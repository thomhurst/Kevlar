using System.Globalization;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kevlar.Extensions.Grpc;

/// <summary>Applies a shared admission shield around the entire lifetime of each server handler.</summary>
/// <remarks>
/// Supports unary, client-streaming, server-streaming, and duplex calls. Shields must guarantee
/// at most one continuation invocation: server request streams and responses cannot be replayed.
/// Strategies must preserve the transport cancellation token. Use gRPC deadlines instead of shield timeouts.
/// The interceptor does not own the supplied shield or partition provider.
/// </remarks>
public sealed class ShieldServerInterceptor : Interceptor
{
    private readonly Shield? _shield;
    private readonly Func<ServerCallContext, ValueTask<Shield>>? _provider;

    /// <summary>Creates an interceptor that shares a single-invocation shield across calls.</summary>
    public ShieldServerInterceptor(Shield shield)
    {
        ValidateShield(shield);
        _shield = shield;
    }

    /// <summary>Creates an interceptor with independent shield state per method, or per selected key.</summary>
    /// <param name="shields">The bounded partition provider, owned by the caller.</param>
    /// <param name="partitionKey">Selects a key from the call context; defaults to its full method name.</param>
    /// <remarks>Use <c>context => context.Peer</c> for per-peer partitions. Each resolved shield must be single-invocation.</remarks>
    public ShieldServerInterceptor(
        PartitionedShield<string> shields,
        Func<ServerCallContext, string>? partitionKey = null)
    {
        if (shields is null) { throw new ArgumentNullException(nameof(shields)); }
        partitionKey ??= static context => context.Method;
        _provider = context => shields.GetShieldAsync(partitionKey(context));
    }

    /// <inheritdoc />
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation) =>
        ExecuteAsync(context, (request, continuation), static (state, call) =>
            new ValueTask<TResponse>(state.continuation(state.request, call)));

    /// <inheritdoc />
    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation) =>
        ExecuteAsync(context, (requestStream, continuation), static (state, call) =>
            new ValueTask<TResponse>(state.continuation(state.requestStream, call)));

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation) =>
        ExecuteAsync(context, (request, responseStream, continuation), static async (state, call) =>
        {
            await state.continuation(state.request, state.responseStream, call).ConfigureAwait(false);
            return true;
        });

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation) =>
        ExecuteAsync(context, (requestStream, responseStream, continuation), static async (state, call) =>
        {
            await state.continuation(state.requestStream, state.responseStream, call).ConfigureAwait(false);
            return true;
        });

    private async Task<TResult> ExecuteAsync<TState, TResult>(
        ServerCallContext context, TState state, Func<TState, ServerCallContext, ValueTask<TResult>> action)
    {
        if (context is null) { throw new ArgumentNullException(nameof(context)); }
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var shield = _shield;
            if (shield is null)
            {
                shield = await _provider!(context).ConfigureAwait(false);
                ValidateShield(shield);
            }
            return await shield.ExecuteAsync((context, state, action), static (execution, token) =>
            {
                // ASP.NET Core activates the service using the original transport context's identity.
                // Replacing it with a token wrapper would break service activation and GetHttpContext().
                if (token != execution.context.CancellationToken)
                {
                    throw new NotSupportedException(
                        "Server admission shields must preserve the transport cancellation token. " +
                        "Use a gRPC deadline instead of a shield timeout or another token-replacing strategy.");
                }
                return execution.action(execution.state, execution.context);
            }, context.CancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyLimitExceededException)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Server concurrency limit exceeded."));
        }
        catch (RateLimitExceededException)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Server rate limit exceeded."));
        }
        catch (CircuitOpenException exception)
        {
            var trailers = new Metadata();
            if (!exception.IsIsolated && exception.RetryAfter is { } retryAfter)
            {
                var milliseconds = (int)Math.Min(int.MaxValue, Math.Max(0, Math.Ceiling(retryAfter.TotalMilliseconds)));
                trailers.Add("grpc-retry-pushback-ms", milliseconds.ToString(CultureInfo.InvariantCulture));
            }
            throw new RpcException(new Status(StatusCode.Unavailable, "Server circuit is open."), trailers);
        }
    }

    private static void ValidateShield(Shield shield)
    {
        if (shield is null) { throw new ArgumentNullException(nameof(shield)); }
        if (!shield.InvokesContinuationAtMostOnce)
        {
            throw new ArgumentException(
                "Server shields must invoke the handler at most once. Retry, hedging, live-forwarding shields, " +
                "and custom strategies without a single-invocation guarantee are not supported.", nameof(shield));
        }
    }

}

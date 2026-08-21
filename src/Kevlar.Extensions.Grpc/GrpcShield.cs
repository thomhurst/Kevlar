using Grpc.Core;

namespace Kevlar.Extensions.Grpc;

/// <summary>Building blocks for opt-in gRPC client resilience.</summary>
public static class GrpcShield
{
    /// <summary>
    /// Returns <see langword="true"/> for the commonly transient gRPC status codes
    /// <see cref="StatusCode.Unavailable"/>, <see cref="StatusCode.DeadlineExceeded"/>, and
    /// <see cref="StatusCode.ResourceExhausted"/>.
    /// </summary>
    public static bool IsTransient(StatusCode statusCode) =>
        statusCode is StatusCode.Unavailable
            or StatusCode.DeadlineExceeded
            or StatusCode.ResourceExhausted;

    /// <summary>
    /// Returns <see langword="true"/> when the exception has a commonly transient status,
    /// or <see langword="false"/> when <paramref name="exception"/> is <see langword="null"/>.
    /// </summary>
    public static bool IsTransient(RpcException? exception) =>
        exception is not null && IsTransient(exception.StatusCode);

    /// <summary>
    /// Starts a shield that handles only <see cref="RpcException"/> instances whose status is
    /// commonly transient. Add retry, circuit-breaker, or hedging strategies explicitly.
    /// </summary>
    public static ShieldBuilder WhenTransient() =>
        Shield.When<RpcException>(IsTransient);
}

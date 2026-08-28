using System.Globalization;
using Grpc.Core;

namespace Kevlar.Extensions.Grpc;

/// <summary>Building blocks for opt-in gRPC client resilience.</summary>
public static class GrpcShield
{
    private const string RetryPushbackMetadataName = "grpc-retry-pushback-ms";

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
    /// Returns <see langword="true"/> when the exception has a commonly transient status and the
    /// server did not explicitly suppress retries through <c>grpc-retry-pushback-ms</c>, or
    /// <see langword="false"/> when <paramref name="exception"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>A negative or malformed pushback value suppresses retries as required by the gRPC retry protocol.</remarks>
    public static bool IsTransient(RpcException? exception) =>
        exception is not null
        && IsTransient(exception.StatusCode)
        && ReadRetryPushback(exception, out _) != RetryPushback.Stop;

    /// <summary>
    /// Starts a shield that handles only <see cref="RpcException"/> instances whose status is
    /// commonly transient. Add retry, circuit-breaker, or hedging strategies explicitly.
    /// </summary>
    public static ShieldBuilder WhenTransient() =>
        Shield.When<RpcException>(IsTransient);

    /// <summary>
    /// A <see cref="RetryOptions.DelayGenerator"/> that uses a valid non-negative
    /// <c>grpc-retry-pushback-ms</c> trailer as the next retry delay.
    /// </summary>
    /// <remarks>
    /// Use with <see cref="WhenTransient"/>, whose predicate suppresses retries for negative or
    /// malformed pushback values. The retry strategy's <see cref="RetryOptions.MaxDelay"/> still
    /// caps the returned delay; when unset, the selected backoff's maximum applies.
    /// </remarks>
    public static ValueTask<TimeSpan?> RetryAfter(RetryEvent retry)
    {
        if (retry.Exception is RpcException exception
            && ReadRetryPushback(exception, out var delay) == RetryPushback.Delay)
        {
            return new(delay);
        }

        return default;
    }

    private static RetryPushback ReadRetryPushback(RpcException exception, out TimeSpan? delay)
    {
        delay = null;
        var value = exception.Trailers.GetValue(RetryPushbackMetadataName);
        if (value is null)
        {
            return RetryPushback.None;
        }

        if (!int.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var milliseconds)
            || !string.Equals(
                value,
                milliseconds.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            || milliseconds < 0)
        {
            return RetryPushback.Stop;
        }

        delay = TimeSpan.FromMilliseconds(milliseconds);
        return RetryPushback.Delay;
    }

    private enum RetryPushback
    {
        None,
        Delay,
        Stop,
    }
}

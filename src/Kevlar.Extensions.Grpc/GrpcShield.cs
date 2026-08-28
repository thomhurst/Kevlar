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

    /// <summary>
    /// A <see cref="RetryOptions.DelayGenerator"/> that uses a valid non-negative
    /// <c>grpc-retry-pushback-ms</c> trailer as the next retry delay.
    /// </summary>
    /// <remarks>
    /// Negative, malformed, or duplicate pushback values suppress additional attempts without
    /// changing the ambient handling clause used by circuit breakers or fallbacks. The retry
    /// strategy's <see cref="RetryOptions.MaxDelay"/> still caps a returned delay; when unset, the
    /// selected backoff's maximum applies. Wrapped and aggregate exception graphs are searched for
    /// the handled <see cref="RpcException"/>.
    /// </remarks>
    [RetryTerminalInspection]
    public static ValueTask<TimeSpan?> RetryAfter(RetryEvent retry)
    {
        retry.RequestTerminalInspection();
        if (retry.Exception is not { } failure
            || FindTransientRpcException(failure) is not { } exception)
        {
            return default;
        }

        var pushback = ReadRetryPushback(exception, out var delay);
        if (pushback == RetryPushback.Stop)
        {
            retry.SuppressAdditionalAttempts();
        }

        if (pushback == RetryPushback.Delay)
        {
            return new(delay);
        }

        return default;
    }

    private static RpcException? FindTransientRpcException(Exception exception)
    {
        Stack<Exception>? pendingBranches = null;
        HashSet<Exception>? visited = null;

        while (true)
        {
            if (visited is not null && !visited.Add(exception))
            {
                if (pendingBranches is null || pendingBranches.Count == 0)
                {
                    return null;
                }

                exception = pendingBranches.Pop();
                continue;
            }

            if (exception is RpcException rpcException && IsTransient(rpcException))
            {
                return rpcException;
            }

            if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            {
                if (visited is null)
                {
                    visited = new HashSet<Exception>(ExceptionReferenceComparer.Instance)
                    {
                        exception,
                    };
                }

                for (var index = aggregate.InnerExceptions.Count - 1; index > 0; index--)
                {
                    (pendingBranches ??= new()).Push(aggregate.InnerExceptions[index]);
                }

                exception = aggregate.InnerExceptions[0];
                continue;
            }

            if (exception.InnerException is { } innerException)
            {
                exception = innerException;
                continue;
            }

            if (pendingBranches is null || pendingBranches.Count == 0)
            {
                return null;
            }

            exception = pendingBranches.Pop();
        }
    }

    private static RetryPushback ReadRetryPushback(RpcException exception, out TimeSpan? delay)
    {
        delay = null;
        using var entries = exception.Trailers.GetAll(RetryPushbackMetadataName).GetEnumerator();
        if (!entries.MoveNext())
        {
            return RetryPushback.None;
        }

        var value = entries.Current.Value;
        if (entries.MoveNext())
        {
            return RetryPushback.Stop;
        }

        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var milliseconds))
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

    private sealed class ExceptionReferenceComparer : IEqualityComparer<Exception>
    {
        public static ExceptionReferenceComparer Instance { get; } = new();

        public bool Equals(Exception? x, Exception? y) => ReferenceEquals(x, y);

        public int GetHashCode(Exception exception) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(exception);
    }
}

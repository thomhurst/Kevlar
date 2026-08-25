using System.Collections.ObjectModel;
using System.Threading.RateLimiting;
using Kevlar.Internal;

namespace Kevlar.Extensions.RateLimiting;

internal sealed class RateLimiterStrategy : Strategy
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyMetadata =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    private readonly RateLimitLeaseAcquirer _acquireLease;
    private readonly int _permitCount;
    private readonly Action<RateLimiterAdapterRejectedEvent>? _onRejected;
    private readonly Func<RateLimiterAdapterRejectedEvent, ValueTask>? _onRejectedAsync;
    private readonly string _description;
    private readonly string _telemetryName;

    protected internal override bool InvokesContinuationAtMostOnce => true;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal RateLimiterStrategy(
        RateLimitLeaseAcquirer acquireLease,
        RateLimiterAdapterOptions options,
        string description)
    {
        if (options.PermitCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PermitCount must be positive.");
        }

        _acquireLease = acquireLease;
        _permitCount = options.PermitCount;
        _onRejected = options.OnRejected;
        _onRejectedAsync = options.OnRejectedAsync;
        _description = description;
        _telemetryName = options.Name ?? "RateLimiterAdapter";
    }

    public override string Describe() => _description;

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        ValueTask<RateLimitLease> acquisition;
        try
        {
            acquisition = _acquireLease(_permitCount, context);
        }
        catch (Exception exception)
        {
            return Failure<T>(exception);
        }

        if (!acquisition.IsCompletedSuccessfully)
        {
            return AwaitAcquisitionAsync(acquisition, next, context);
        }

        return ExecuteLease(acquisition.Result, next, context);
    }

    private async ValueTask<Outcome<T>> AwaitAcquisitionAsync<T, TState>(
        ValueTask<RateLimitLease> acquisition,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        RateLimitLease lease;
        try
        {
            lease = await acquisition.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }

        return await ExecuteLease(lease, next, context).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> ExecuteLease<T, TState>(
        RateLimitLease lease,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        if (context.CancellationToken.IsCancellationRequested)
        {
            return DisposeWithFailure<T>(
                lease,
                new OperationCanceledException(context.CancellationToken));
        }

        bool isAcquired;
        try
        {
            isAcquired = lease.IsAcquired;
        }
        catch (Exception exception)
        {
            return DisposeWithFailure<T>(lease, exception);
        }

        if (!isAcquired)
        {
            return RejectLease<T>(lease, context);
        }

        ValueTask<Outcome<T>> execution;
        try
        {
            execution = next.InvokeAsync(context);
        }
        catch (Exception exception)
        {
            return DisposeWithFailure<T>(lease, exception);
        }

        if (!execution.IsCompletedSuccessfully)
        {
            return AwaitExecutionAsync(execution, lease);
        }

        var outcome = execution.Result;
        try
        {
            lease.Dispose();
        }
        catch (Exception exception)
        {
            return Failure<T>(exception);
        }

        return new ValueTask<Outcome<T>>(outcome);
    }

    private ValueTask<Outcome<T>> RejectLease<T>(RateLimitLease lease, KevlarContext context)
    {
        TimeSpan? retryAfter;
        IReadOnlyDictionary<string, object?> metadata;
        try
        {
            retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan value)
                ? value
                : null;
            metadata = SnapshotMetadata(lease);
        }
        catch (Exception exception)
        {
            return DisposeWithFailure<T>(lease, exception);
        }

        try
        {
            lease.Dispose();
        }
        catch (Exception exception)
        {
            return Failure<T>(exception);
        }

        KevlarMetrics.Rejection(context, "rate_limiter_adapter", _telemetryName);
        var rejection = new RateLimiterAdapterRejectedException(retryAfter);
        if (_onRejected is null && _onRejectedAsync is null)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
        }

        var rejectedEvent = new RateLimiterAdapterRejectedEvent(
            retryAfter,
            metadata,
            _permitCount,
            context);
        CallbackInvoker.Invoke(
            _onRejected,
            rejectedEvent,
            CallbackErrorKind.RateLimiterAdapterRejected,
            context);
        var notification = CallbackInvoker.InvokeAsync(
            _onRejectedAsync,
            rejectedEvent,
            CallbackErrorKind.RateLimiterAdapterRejected,
            context);
        if (notification.IsCompletedSuccessfully)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
        }

        return AwaitRejectionAsync<T>(notification, rejection);
    }

    private static IReadOnlyDictionary<string, object?> SnapshotMetadata(RateLimitLease lease)
    {
        Dictionary<string, object?>? snapshot = null;
        foreach (var pair in lease.GetAllMetadata())
        {
            (snapshot ??= new Dictionary<string, object?>(StringComparer.Ordinal))[pair.Key] = pair.Value;
        }

        return snapshot is null
            ? EmptyMetadata
            : new ReadOnlyDictionary<string, object?>(snapshot);
    }

    private static async ValueTask<Outcome<T>> AwaitExecutionAsync<T>(
        ValueTask<Outcome<T>> execution,
        RateLimitLease lease)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static async ValueTask<Outcome<T>> AwaitRejectionAsync<T>(
        ValueTask notification,
        RateLimiterAdapterRejectedException rejection)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromException(rejection);
    }

    private static ValueTask<Outcome<T>> DisposeWithFailure<T>(
        RateLimitLease lease,
        Exception failure)
    {
        try
        {
            lease.Dispose();
        }
        catch (Exception disposalFailure)
        {
            failure = new AggregateException(failure, disposalFailure).Flatten();
        }

        return Failure<T>(failure);
    }

    private static ValueTask<Outcome<T>> Failure<T>(Exception exception) =>
        new(Outcome<T>.FromException(exception));
}

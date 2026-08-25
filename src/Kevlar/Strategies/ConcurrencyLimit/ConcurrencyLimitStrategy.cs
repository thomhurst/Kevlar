using System.Runtime.ExceptionServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class ConcurrencyLimitStrategy : Strategy
{
    private readonly Lock _metricsPublicationGate = new();
    private readonly HashSet<StrategyMetricAlias> _metricsAliases = [];
    // Atomic permits serve the uncontended path; the semaphore carries permits only to registered waiters.
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly int _queueLimit;
    private readonly long _capacity;
    private readonly Action<ConcurrencyLimitRejectedEvent>? _onRejected;
    private readonly Func<ConcurrencyLimitRejectedEvent, ValueTask>? _onRejectedAsync;
    private int _available;
    private int _waiters;
    private StrategyMetricAlias[] _metricsAliasSnapshot = [];
    private long _pending;
    private long _metricsState;

    protected internal override bool InvokesContinuationAtMostOnce => true;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal int MaxConcurrency => _maxConcurrency;

    internal int QueueLimit => _queueLimit;

    internal bool HasNotification => _onRejected is not null || _onRejectedAsync is not null;

    internal (int Available, int Running, int Queued) CaptureState()
    {
        var state = Volatile.Read(ref _metricsState);
        var running = (int)(state >> 32);
        var queued = (int)(state & uint.MaxValue);
        return (_maxConcurrency - running, running, queued);
    }

    public ConcurrencyLimitStrategy(ConcurrencyLimitOptions options)
    {
        Throw.IfOutOfRange(options.MaxConcurrency <= 0, nameof(options), "MaxConcurrency must be positive.");
        Throw.IfOutOfRange(options.QueueLimit < 0, nameof(options), "QueueLimit must not be negative.");
        _semaphore = new SemaphoreSlim(0, options.MaxConcurrency);
        _maxConcurrency = options.MaxConcurrency;
        _queueLimit = options.QueueLimit;
        _capacity = options.MaxConcurrency + (long)options.QueueLimit;
        _available = options.MaxConcurrency;
        _onRejected = options.OnRejected;
        _onRejectedAsync = options.OnRejectedAsync;
    }

    public override string Describe() =>
        _queueLimit > 0 ? $"ConcurrencyLimit({_maxConcurrency}, queue {_queueLimit})" : $"ConcurrencyLimit({_maxConcurrency})";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var alias = new StrategyMetricAlias(context.ShieldName, context.StrategyIndex);
        if (Interlocked.Increment(ref _pending) > _capacity)
        {
            Interlocked.Decrement(ref _pending);
            RecordState(alias);
            KevlarMetrics.Rejection(context.ShieldName, "concurrency_limit");
            return RejectAsync<T>(context);
        }

        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (TryAcquirePermit())
            {
                return ExecuteAcquired(next, context, alias, queued: false);
            }
        }
        catch (OperationCanceledException cancelled)
        {
            Interlocked.Decrement(ref _pending);
            RecordState(alias);
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(NormalizeCancellation(cancelled, context)));
        }

        return ExecuteQueuedAsync(next, context, alias);
    }

    private ValueTask<Outcome<T>> RejectAsync<T>(KevlarContext context)
    {
        var rejection = new ConcurrencyLimitExceededException();
        if (_onRejected is null && _onRejectedAsync is null)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
        }

        var rejectedEvent = new ConcurrencyLimitRejectedEvent(
            _maxConcurrency,
            _queueLimit,
            context);
        try
        {
            _onRejected?.Invoke(rejectedEvent);
            if (_onRejectedAsync is null)
            {
                return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
            }

            var notification = _onRejectedAsync(rejectedEvent);
            if (notification.IsCompletedSuccessfully)
            {
                notification.GetAwaiter().GetResult();
                return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
            }

            return AwaitRejectionAsync<T>(notification, rejection);
        }
        catch (Exception callbackFailure)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(callbackFailure));
        }
    }

    private static async ValueTask<Outcome<T>> AwaitRejectionAsync<T>(
        ValueTask notification,
        ConcurrencyLimitExceededException rejection)
    {
        try
        {
            await notification.ConfigureAwait(false);
            return Outcome<T>.FromException(rejection);
        }
        catch (Exception callbackFailure)
        {
            return Outcome<T>.FromException(callbackFailure);
        }
    }

    private async ValueTask<Outcome<T>> ExecuteQueuedAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        StrategyMetricAlias alias)
    {
        UpdateMetricsState(inflightDelta: 0, queuedDelta: 1);
        Interlocked.Increment(ref _waiters);
        try
        {
            if (!TryAcquirePermit())
            {
                if (context.IsSynchronous)
                {
                    RecordState(alias);
                    _semaphore.Wait(context.CancellationToken);
                }
                else
                {
                    using var waitCancellation = context.CancellationToken.CanBeCanceled
                        ? CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken)
                        : new CancellationTokenSource();
                    var wait = _semaphore.WaitAsync(waitCancellation.Token);
                    try
                    {
                        RecordState(alias);
                    }
                    catch (Exception publicationFailure)
                    {
                        waitCancellation.Cancel();
                        try
                        {
                            await wait.ConfigureAwait(false);
                            ReleasePermit();
                        }
                        catch (OperationCanceledException)
                        {
                            // Cancellation withdrew the wait before it took a permit.
                        }

                        ExceptionDispatchInfo.Capture(publicationFailure).Throw();
                    }

                    await wait.ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException cancelled)
        {
            ReleaseQueued(alias);
            return Outcome<T>.FromException(NormalizeCancellation(cancelled, context));
        }
        catch (Exception publicationFailure)
        {
            ReleaseQueued(alias, publicationFailure);
            ExceptionDispatchInfo.Capture(publicationFailure).Throw();
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _waiters);
        }

        return await ExecuteAcquired(next, context, alias, queued: true).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> ExecuteAcquired<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        StrategyMetricAlias alias,
        bool queued)
    {
        UpdateMetricsState(inflightDelta: 1, queuedDelta: queued ? -1 : 0);
        try
        {
            RecordState(alias);
        }
        catch (Exception publicationFailure)
        {
            UpdateMetricsState(inflightDelta: -1, queuedDelta: 0);
            ReleasePermit();
            Interlocked.Decrement(ref _pending);
            try
            {
                RecordState(alias);
            }
            catch (Exception correctionFailure)
            {
                publicationFailure = new AggregateException(
                    publicationFailure,
                    correctionFailure).Flatten();
            }

            ExceptionDispatchInfo.Capture(publicationFailure).Throw();
            throw;
        }

        var execution = next.InvokeAsync(context);
        if (!execution.IsCompletedSuccessfully)
        {
            return AwaitExecutionAsync(execution, alias);
        }

        var outcome = execution.Result;
        CompleteExecution(alias);
        return new ValueTask<Outcome<T>>(outcome);
    }

    private async ValueTask<Outcome<T>> AwaitExecutionAsync<T>(
        ValueTask<Outcome<T>> execution,
        StrategyMetricAlias alias)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            CompleteExecution(alias);
        }
    }

    private void CompleteExecution(StrategyMetricAlias alias)
    {
        UpdateMetricsState(inflightDelta: -1, queuedDelta: 0);
        ReleasePermit();
        Interlocked.Decrement(ref _pending);
        RecordState(alias);
    }

    private void ReleaseQueued(
        StrategyMetricAlias alias,
        Exception? publicationFailure = null)
    {
        Interlocked.Decrement(ref _pending);
        UpdateMetricsState(inflightDelta: 0, queuedDelta: -1);

        try
        {
            RecordState(alias);
        }
        catch (Exception correctionFailure) when (publicationFailure is not null)
        {
            throw new AggregateException(publicationFailure, correctionFailure).Flatten();
        }
    }

    private static OperationCanceledException NormalizeCancellation(
        OperationCanceledException cancelled,
        KevlarContext context) =>
        context.CancellationToken.IsCancellationRequested
        && cancelled.CancellationToken != context.CancellationToken
            ? new OperationCanceledException(
                cancelled.Message,
                cancelled,
                context.CancellationToken)
            : cancelled;

    private bool TryAcquirePermit()
    {
        while (true)
        {
            var available = Volatile.Read(ref _available);
            if (available == 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _available, available - 1, available) == available)
            {
                return true;
            }
        }
    }

    private void ReleasePermit()
    {
        // Publish first. A waiter that registers concurrently either claims this permit on its
        // atomic retry, or leaves it here for this release to transfer to the semaphore.
        Interlocked.Increment(ref _available);
        if (Volatile.Read(ref _waiters) > 0 && TryAcquirePermit())
        {
            _semaphore.Release();
        }
    }

    private void UpdateMetricsState(int inflightDelta, int queuedDelta)
    {
        while (true)
        {
            var state = Volatile.Read(ref _metricsState);
            var inflight = (int)(state >> 32);
            var queued = (int)(state & uint.MaxValue);
            var updated = ((long)(inflight + inflightDelta) << 32)
                | (uint)(queued + queuedDelta);
            if (Interlocked.CompareExchange(ref _metricsState, updated, state) == state)
            {
                return;
            }
        }
    }

    private void RecordState(StrategyMetricAlias alias)
    {
        if (!KevlarMetrics.ConcurrencyStateEnabled)
        {
            return;
        }

        lock (_metricsPublicationGate)
        {
            if (_metricsAliases.Count < KevlarMetrics.MaxTrackedStrategyAliases
                && _metricsAliases.Add(alias))
            {
                _metricsAliasSnapshot = [.. _metricsAliases];
            }

            while (true)
            {
                var state = Volatile.Read(ref _metricsState);
                var inflight = (int)(state >> 32);
                var queued = (int)(state & uint.MaxValue);
                var aliases = _metricsAliasSnapshot;
                RecordStateForAliases(aliases, inflight, queued);

                if (state == Volatile.Read(ref _metricsState)
                    && ReferenceEquals(aliases, _metricsAliasSnapshot))
                {
                    return;
                }
            }
        }
    }

    private void RecordStateForAliases(StrategyMetricAlias[] aliases, int inflight, int queued)
    {
        List<Exception>? failures = null;
        foreach (var alias in aliases)
        {
            try
            {
                KevlarMetrics.RecordConcurrencyState(
                    alias.ShieldName,
                    alias.StrategyIndex,
                    inflight,
                    queued,
                    _maxConcurrency);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is [var failure])
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures).Flatten();
        }
    }
}

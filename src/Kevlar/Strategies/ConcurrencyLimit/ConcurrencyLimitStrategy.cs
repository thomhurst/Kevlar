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
    private readonly int _maxQueue;
    private readonly long _capacity;
    private int _available;
    private int _waiters;
    private StrategyMetricAlias[] _metricsAliasSnapshot = [];
    private long _pending;
    private long _metricsState;

    internal override bool InvokesContinuationAtMostOnce => true;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal int MaxConcurrency => _maxConcurrency;

    internal int MaxQueue => _maxQueue;

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
        Throw.IfOutOfRange(options.MaxQueue < 0, nameof(options), "MaxQueue must not be negative.");
        _semaphore = new SemaphoreSlim(0, options.MaxConcurrency);
        _maxConcurrency = options.MaxConcurrency;
        _maxQueue = options.MaxQueue;
        _capacity = options.MaxConcurrency + (long)options.MaxQueue;
        _available = options.MaxConcurrency;
    }

    public override string Describe() =>
        _maxQueue > 0 ? $"ConcurrencyLimit({_maxConcurrency}, queue {_maxQueue})" : $"ConcurrencyLimit({_maxConcurrency})";

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var alias = new StrategyMetricAlias(context.ShieldName, context.StrategyIndex);
        if (Interlocked.Increment(ref _pending) > _capacity)
        {
            Interlocked.Decrement(ref _pending);
            RecordState(alias);
            KevlarMetrics.Rejection(context.ShieldName, "concurrency_limit");
            return Outcome<T>.FromException(new ConcurrencyLimitExceededException());
        }

        var queued = false;
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!TryAcquirePermit())
            {
                queued = true;
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
                finally
                {
                    Interlocked.Decrement(ref _waiters);
                }
            }
        }
        catch (OperationCanceledException cancelled)
        {
            if (context.CancellationToken.IsCancellationRequested
                && cancelled.CancellationToken != context.CancellationToken)
            {
                cancelled = new OperationCanceledException(
                    cancelled.Message,
                    cancelled,
                    context.CancellationToken);
            }

            Interlocked.Decrement(ref _pending);
            if (queued)
            {
                UpdateMetricsState(inflightDelta: 0, queuedDelta: -1);
            }

            RecordState(alias);
            return Outcome<T>.FromException(cancelled);
        }
        catch (Exception publicationFailure)
        {
            Interlocked.Decrement(ref _pending);
            if (queued)
            {
                UpdateMetricsState(inflightDelta: 0, queuedDelta: -1);
            }

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

        try
        {
            return await next.InvokeAsync(context).ConfigureAwait(false);
        }
        finally
        {
            UpdateMetricsState(inflightDelta: -1, queuedDelta: 0);
            ReleasePermit();
            Interlocked.Decrement(ref _pending);
            RecordState(alias);
        }
    }

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

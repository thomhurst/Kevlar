using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class ConcurrencyLimitStrategy : Strategy
{
    private readonly Lock _metricsPublicationGate = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly int _maxQueue;
    private readonly long _capacity;
    private readonly string _metricsInstanceId = KevlarMetrics.CreateStrategyInstanceId();
    private long _pending;
    private long _metricsState;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    public ConcurrencyLimitStrategy(ConcurrencyLimitOptions options)
    {
        Throw.IfOutOfRange(options.MaxConcurrency <= 0, nameof(options), "MaxConcurrency must be positive.");
        Throw.IfOutOfRange(options.MaxQueue < 0, nameof(options), "MaxQueue must not be negative.");
        _semaphore = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        _maxConcurrency = options.MaxConcurrency;
        _maxQueue = options.MaxQueue;
        _capacity = options.MaxConcurrency + (long)options.MaxQueue;
    }

    public override string Describe() =>
        _maxQueue > 0 ? $"ConcurrencyLimit({_maxConcurrency}, queue {_maxQueue})" : $"ConcurrencyLimit({_maxConcurrency})";

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (Interlocked.Increment(ref _pending) > _capacity)
        {
            Interlocked.Decrement(ref _pending);
            RecordState(context.ShieldName);
            KevlarMetrics.Rejection(context.ShieldName, "concurrency_limit");
            return Outcome<T>.FromException(new ConcurrencyLimitExceededException());
        }

        var queued = false;
        try
        {
            if (context.IsSynchronous)
            {
                if (!_semaphore.Wait(0, context.CancellationToken))
                {
                    queued = true;
                    UpdateMetricsState(inflightDelta: 0, queuedDelta: 1);
                    RecordState(context.ShieldName);
                    _semaphore.Wait(context.CancellationToken);
                }
            }
            else
            {
                var wait = _semaphore.WaitAsync(context.CancellationToken);
                if (!wait.IsCompleted)
                {
                    queued = true;
                    UpdateMetricsState(inflightDelta: 0, queuedDelta: 1);
                    RecordState(context.ShieldName);
                }

                await wait.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException cancelled)
        {
            Interlocked.Decrement(ref _pending);
            if (queued)
            {
                UpdateMetricsState(inflightDelta: 0, queuedDelta: -1);
            }

            RecordState(context.ShieldName);
            return Outcome<T>.FromException(cancelled);
        }
        catch
        {
            Interlocked.Decrement(ref _pending);
            if (queued)
            {
                UpdateMetricsState(inflightDelta: 0, queuedDelta: -1);
            }

            throw;
        }

        UpdateMetricsState(inflightDelta: 1, queuedDelta: queued ? -1 : 0);
        try
        {
            RecordState(context.ShieldName);
        }
        catch
        {
            _semaphore.Release();
            Interlocked.Decrement(ref _pending);
            UpdateMetricsState(inflightDelta: -1, queuedDelta: 0);
            throw;
        }

        try
        {
            return await next.InvokeAsync(context).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
            Interlocked.Decrement(ref _pending);
            UpdateMetricsState(inflightDelta: -1, queuedDelta: 0);
            RecordState(context.ShieldName);
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

    private void RecordState(string? shieldName)
    {
        if (!KevlarMetrics.ConcurrencyStateEnabled)
        {
            return;
        }

        lock (_metricsPublicationGate)
        {
            while (true)
            {
                var state = Volatile.Read(ref _metricsState);
                var inflight = (int)(state >> 32);
                var queued = (int)(state & uint.MaxValue);

                KevlarMetrics.RecordConcurrencyState(
                    shieldName,
                    _metricsInstanceId,
                    inflight,
                    queued,
                    _maxConcurrency);

                if (state == Volatile.Read(ref _metricsState))
                {
                    return;
                }
            }
        }
    }
}

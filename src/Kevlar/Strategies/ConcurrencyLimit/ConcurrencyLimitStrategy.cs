using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class ConcurrencyLimitStrategy : Strategy
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly int _maxQueue;
    private readonly long _capacity;
    private long _pending;

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

        try
        {
            if (context.IsSynchronous)
            {
                if (!_semaphore.Wait(0, context.CancellationToken))
                {
                    RecordState(context.ShieldName);
                    _semaphore.Wait(context.CancellationToken);
                }
            }
            else
            {
                var wait = _semaphore.WaitAsync(context.CancellationToken);
                if (!wait.IsCompleted)
                {
                    RecordState(context.ShieldName);
                }

                await wait.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException cancelled)
        {
            Interlocked.Decrement(ref _pending);
            RecordState(context.ShieldName);
            return Outcome<T>.FromException(cancelled);
        }

        RecordState(context.ShieldName);

        try
        {
            return await next.InvokeAsync(context).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
            Interlocked.Decrement(ref _pending);
            RecordState(context.ShieldName);
        }
    }

    private void RecordState(string? shieldName)
    {
        if (!KevlarMetrics.ConcurrencyStateEnabled)
        {
            return;
        }

        var pending = Volatile.Read(ref _pending);
        var inflight = _maxConcurrency - _semaphore.CurrentCount;
        var queued = Math.Max(0, pending - inflight);
        KevlarMetrics.RecordConcurrencyState(shieldName, inflight, queued, _maxConcurrency);
    }
}

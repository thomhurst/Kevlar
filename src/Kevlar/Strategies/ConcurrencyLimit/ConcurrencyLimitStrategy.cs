using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class ConcurrencyLimitStrategy : Strategy
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly int _maxQueue;
    private readonly int _capacity;
    private int _pending;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    public ConcurrencyLimitStrategy(ConcurrencyLimitOptions options)
    {
        Throw.IfOutOfRange(options.MaxConcurrency <= 0, nameof(options), "MaxConcurrency must be positive.");
        Throw.IfOutOfRange(options.MaxQueue < 0, nameof(options), "MaxQueue must not be negative.");
        Throw.IfOutOfRange(options.MaxQueue > int.MaxValue - options.MaxConcurrency, nameof(options.MaxQueue), "MaxConcurrency plus MaxQueue must not exceed Int32.MaxValue.");

        _semaphore = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        _maxConcurrency = options.MaxConcurrency;
        _maxQueue = options.MaxQueue;
        _capacity = options.MaxConcurrency + options.MaxQueue;
    }

    public override string Describe() =>
        _maxQueue > 0 ? $"ConcurrencyLimit({_maxConcurrency}, queue {_maxQueue})" : $"ConcurrencyLimit({_maxConcurrency})";

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (Interlocked.Increment(ref _pending) > _capacity)
        {
            Interlocked.Decrement(ref _pending);
            KevlarMetrics.Rejection(context.ShieldName, "concurrency_limit");
            return Outcome<T>.FromException(new ConcurrencyLimitExceededException());
        }

        try
        {
            if (context.IsSynchronous)
            {
                _semaphore.Wait(context.CancellationToken);
            }
            else
            {
                await _semaphore.WaitAsync(context.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException cancelled)
        {
            Interlocked.Decrement(ref _pending);
            return Outcome<T>.FromException(cancelled);
        }

        try
        {
            return await next.InvokeAsync(context).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
            Interlocked.Decrement(ref _pending);
        }
    }
}

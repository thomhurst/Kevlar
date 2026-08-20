using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class BulkheadStrategy : Strategy
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _capacity;
    private int _pending;

    public BulkheadStrategy(BulkheadOptions options)
    {
        Throw.IfOutOfRange(options.MaxConcurrency <= 0, nameof(options), "MaxConcurrency must be positive.");
        Throw.IfOutOfRange(options.MaxQueue < 0, nameof(options), "MaxQueue must not be negative.");

        _semaphore = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        _capacity = options.MaxConcurrency + options.MaxQueue;
    }

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (Interlocked.Increment(ref _pending) > _capacity)
        {
            Interlocked.Decrement(ref _pending);
            return Outcome<T>.FromException(new BulkheadRejectedException());
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

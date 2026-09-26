using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Kevlar.Internal;

/// <summary>Owns bounded priority queue admission for the opt-in limiter strategies.</summary>
internal abstract class PriorityLimitStrategy(int queueLimit, TimeSpan? queueTimeout) : Strategy
{
    protected readonly object Gate = new();
    private Entry? _head;
    private Entry? _tail;
    protected int Queued { get; private set; }

    protected internal override bool InvokesContinuationAtMostOnce => true;
    protected internal override bool IsDuplicateReferenceUnsafe => true;

    // All admission and queue mutations occur under Gate. Callbacks and user code never do.
    protected abstract bool TryAcquire(TimeProvider timeProvider, out TimeSpan? retryAfter);
    protected abstract void Release();
    protected abstract bool ReleasesAfterExecution { get; }
    protected abstract void RegisterMetrics(KevlarContext context);
    protected abstract ValueTask<Outcome<T>> RejectAsync<T>(KevlarContext context, TimeSpan? retryAfter, string? reason);

    public sealed override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        RegisterMetrics(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        Entry? entry = null;
        var admitted = false;
        TimeSpan? retryAfter = null;
        lock (Gate)
        {
            if (_head is null && TryAcquire(context.TimeProvider, out retryAfter))
            {
                admitted = true;
            }
            else
            {
                context.Properties.TryGet(KevlarKeys.Priority, out int priority);
                if (Queued < queueLimit || (_tail is not null && priority > _tail.Priority))
                {
                    entry = new Entry(priority);
                    if (Queued == queueLimit)
                    {
                        var evicted = _tail!;
                        Remove(evicted);
                        evicted.Evicted = true;
                        evicted.Signal();
                    }
                    Enqueue(entry);
                }
            }
        }

        if (admitted)
        {
            return ExecuteAcquired(next, context);
        }
        return entry is null
            ? RejectAsync<T>(context, retryAfter, reason: null)
            : ExecuteQueuedAsync(next, context, entry);
    }

    private async ValueTask<Outcome<T>> ExecuteQueuedAsync<T, TState>(
        Continuation<T, TState> next, KevlarContext context, Entry entry)
    {
        QueueWaitTimeout? timeout = null;
        try
        {
            timeout = queueTimeout is { } duration ? new QueueWaitTimeout(context, duration) : null;
            var token = timeout?.Token ?? context.CancellationToken;
            while (true)
            {
                Task changed;
                TimeSpan wait;
                bool evicted;
                lock (Gate)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    timeout?.ThrowIfCancellationRequested();
                    evicted = entry.Evicted;
                    TimeSpan? retryAfter = null;
                    if (!evicted && ReferenceEquals(_head, entry)
                        && TryAcquire(context.TimeProvider, out retryAfter))
                    {
                        Remove(entry);
                        break;
                    }
                    wait = retryAfter ?? Timeout.InfiniteTimeSpan;
                    changed = entry.Changed.Task;
                }
                if (evicted)
                {
                    return await RejectAsync<T>(context, retryAfter: null, reason: "queue_evicted").ConfigureAwait(false);
                }
                var waiting = WaitForChangeAsync(changed, context.TimeProvider, wait, token);
                if (context.IsSynchronous)
                {
                    waiting.GetAwaiter().GetResult();
                }
                else
                {
                    await waiting.ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException cancelled)
        {
            Cancel(entry);
            if (timeout?.Token.IsCancellationRequested == true && !context.CancellationToken.IsCancellationRequested)
            {
                return await RejectAsync<T>(context, retryAfter: null, reason: "queue_timeout").ConfigureAwait(false);
            }
            return Outcome<T>.FromException(context.CancellationToken.IsCancellationRequested
                ? new OperationCanceledException(cancelled.Message, cancelled, context.CancellationToken)
                : cancelled);
        }
        catch (Exception exception)
        {
            Cancel(entry);
            return Outcome<T>.FromException(exception);
        }
        finally
        {
            timeout?.Dispose();
        }
        return await ExecuteAcquired(next, context).ConfigureAwait(false);
    }

    private static async Task WaitForChangeAsync(Task changed, TimeProvider timeProvider, TimeSpan wait, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var delay = DelayHelper.CreateDelayTask(timeProvider, DelayHelper.Clamp(wait), cancellation.Token);
            await Task.WhenAny(changed, delay).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            // Dispose the losing delay when reordering or admission wakes this waiter.
            cancellation.Cancel();
        }
    }

    private ValueTask<Outcome<T>> ExecuteAcquired<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (!ReleasesAfterExecution)
        {
            return next.InvokeAsync(context);
        }
        ValueTask<Outcome<T>> execution;
        try
        {
            execution = next.InvokeAsync(context);
        }
        catch
        {
            CompleteExecution();
            throw;
        }
        if (!execution.IsCompletedSuccessfully)
        {
            return AwaitExecutionAsync(execution);
        }
        var outcome = execution.Result;
        CompleteExecution();
        return new ValueTask<Outcome<T>>(outcome);
    }

#if NET8_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private async ValueTask<Outcome<T>> AwaitExecutionAsync<T>(ValueTask<Outcome<T>> execution)
    {
        try
        {
            return await execution.ConfigureAwait(false);
        }
        finally
        {
            CompleteExecution();
        }
    }

    private void CompleteExecution()
    {
        lock (Gate)
        {
            Release();
            _head?.Signal();
        }
    }

    protected IReadOnlyDictionary<int, int> CapturePriorities()
    {
        var counts = new Dictionary<int, int>();
        for (var entry = _head; entry is not null; entry = entry.Next)
        {
            counts.TryGetValue(entry.Priority, out var count);
            counts[entry.Priority] = count + 1;
        }
        return new ReadOnlyDictionary<int, int>(counts);
    }

    private void Cancel(Entry entry)
    {
        lock (Gate)
        {
            if (entry.IsQueued)
            {
                Remove(entry);
            }
        }
    }

    private void Enqueue(Entry entry)
    {
        var previous = _tail;
        while (previous is not null && previous.Priority < entry.Priority)
        {
            previous = previous.Previous;
        }
        entry.Previous = previous;
        entry.Next = previous is null ? _head : previous.Next;
        if (entry.Next is null)
        {
            _tail = entry;
        }
        else
        {
            entry.Next.Previous = entry;
        }
        if (previous is null)
        {
            _head?.Signal();
            _head = entry;
        }
        else
        {
            previous.Next = entry;
        }
        Queued++;
    }

    private void Remove(Entry entry)
    {
        if (entry.Previous is null)
        {
            _head = entry.Next;
            _head?.Signal();
        }
        else
        {
            entry.Previous.Next = entry.Next;
        }
        if (entry.Next is null)
        {
            _tail = entry.Previous;
        }
        else
        {
            entry.Next.Previous = entry.Previous;
        }
        entry.Previous = null;
        entry.Next = null;
        entry.IsQueued = false;
        Queued--;
    }

    private sealed class Entry(int priority)
    {
        public int Priority { get; } = priority;
        public Entry? Previous { get; set; }
        public Entry? Next { get; set; }
        public bool IsQueued { get; set; } = true;
        public bool Evicted { get; set; }
        public TaskCompletionSource<bool> Changed { get; private set; } = NewSignal();

        public void Signal()
        {
            Changed.TrySetResult(true);
            Changed = NewSignal();
        }

        private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

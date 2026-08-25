using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class ConcurrencyLimitStrategy : Strategy
{
    // Atomic permits serve the uncontended path; the semaphore carries permits only to registered waiters.
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly int _queueLimit;
    private readonly long _capacity;
    private readonly Action<ConcurrencyLimitRejectedEvent>? _onRejected;
    private readonly Func<ConcurrencyLimitRejectedEvent, ValueTask>? _onRejectedAsync;
    private readonly string _telemetryName;
    private int _available;
    private int _queued;
    private int _running;
    private int _waiters;
    private long _pending;
    private readonly KevlarMetrics.StateMetricRegistration<ConcurrencyLimitStrategy> _metricsRegistration;

    protected internal override bool InvokesContinuationAtMostOnce => true;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal int MaxConcurrency => _maxConcurrency;

    internal int QueueLimit => _queueLimit;

    internal bool HasNotification => _onRejected is not null || _onRejectedAsync is not null;

    internal (int Available, int Running, int Queued) CaptureState()
    {
        var running = Math.Min(_maxConcurrency, Math.Max(0, Volatile.Read(ref _running)));
        var queued = Math.Min(_queueLimit, Math.Max(0, Volatile.Read(ref _queued)));
        return (_maxConcurrency - running, running, queued);
    }

    public ConcurrencyLimitStrategy(ConcurrencyLimitOptions options)
    {
        ConfigurationValidation.ThrowIf(
            options.MaxConcurrency <= 0,
            typeof(ConcurrencyLimitOptions),
            nameof(options.MaxConcurrency),
            options.MaxConcurrency,
            "must be positive");
        ConfigurationValidation.ThrowIf(
            options.QueueLimit < 0,
            typeof(ConcurrencyLimitOptions),
            nameof(options.QueueLimit),
            options.QueueLimit,
            "must not be negative");
        _semaphore = new SemaphoreSlim(0, options.MaxConcurrency);
        _maxConcurrency = options.MaxConcurrency;
        _queueLimit = options.QueueLimit;
        _capacity = options.MaxConcurrency + (long)options.QueueLimit;
        _available = options.MaxConcurrency;
        _onRejected = options.OnRejected;
        _onRejectedAsync = options.OnRejectedAsync;
        _telemetryName = options.Name ?? "ConcurrencyLimit";
    }

    public override string Describe() =>
        _queueLimit > 0 ? $"ConcurrencyLimit({_maxConcurrency}, queue {_queueLimit})" : $"ConcurrencyLimit({_maxConcurrency})";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var alias = new StrategyMetricAlias(context.ShieldName, context.StrategyIndex);
        RegisterMetricsAlias(alias);
        if (!TryReserveCapacity())
        {
            Interlocked.Decrement(ref _pending);
            RecordState(alias);
            return RejectAsync<T>(context);
        }

        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (TryAcquirePermit())
            {
                Interlocked.Increment(ref _running);
                return ExecuteAcquired(next, context);
            }
        }
        catch (OperationCanceledException cancelled)
        {
            Interlocked.Decrement(ref _pending);
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(NormalizeCancellation(cancelled, context)));
        }

        return ExecuteQueuedAsync(next, context);
    }

    private ValueTask<Outcome<T>> RejectAsync<T>(KevlarContext context)
    {
        var rejection = new ConcurrencyLimitExceededException();
        KevlarMetrics.Rejection(context, "concurrency_limit", rejection, _telemetryName);
        if (_onRejected is null && _onRejectedAsync is null)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
        }

        var rejectedEvent = new ConcurrencyLimitRejectedEvent(
            _maxConcurrency,
            _queueLimit,
            context);
        CallbackInvoker.Invoke(
            _onRejected,
            rejectedEvent,
            CallbackErrorKind.ConcurrencyLimitRejected,
            context);
        var notification = CallbackInvoker.InvokeAsync(
            _onRejectedAsync,
            rejectedEvent,
            CallbackErrorKind.ConcurrencyLimitRejected,
            context);
        if (notification.IsCompletedSuccessfully)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection));
        }

        return AwaitRejectionAsync<T>(notification, rejection);
    }

    private static async ValueTask<Outcome<T>> AwaitRejectionAsync<T>(
        ValueTask notification,
        ConcurrencyLimitExceededException rejection)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromException(rejection);
    }

    private async ValueTask<Outcome<T>> ExecuteQueuedAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        Interlocked.Increment(ref _queued);
        Interlocked.Increment(ref _waiters);
        try
        {
            if (!TryAcquirePermit())
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

            Interlocked.Increment(ref _running);
        }
        catch (OperationCanceledException cancelled)
        {
            Interlocked.Decrement(ref _pending);
            return Outcome<T>.FromException(NormalizeCancellation(cancelled, context));
        }
        finally
        {
            Interlocked.Decrement(ref _waiters);
            Interlocked.Decrement(ref _queued);
        }

        return await ExecuteAcquired(next, context).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> ExecuteAcquired<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var execution = next.InvokeAsync(context);
        if (!execution.IsCompletedSuccessfully)
        {
            return AwaitExecutionAsync(execution);
        }

        var outcome = execution.Result;
        CompleteExecution();
        return new ValueTask<Outcome<T>>(outcome);
    }

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
        Interlocked.Decrement(ref _running);
        Interlocked.Decrement(ref _pending);
        ReleasePermit();
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

    private bool TryReserveCapacity()
    {
        while (true)
        {
            var pending = Volatile.Read(ref _pending);
            if (pending >= _capacity)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _pending, pending + 1, pending) == pending)
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

    private void RegisterMetricsAlias(StrategyMetricAlias alias)
    {
        if (KevlarMetrics.ConcurrencyStateEnabled)
        {
            _metricsRegistration.Add(alias);
        }
    }
}

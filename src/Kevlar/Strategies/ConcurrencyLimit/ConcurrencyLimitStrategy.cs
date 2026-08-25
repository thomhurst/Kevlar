using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class ConcurrencyLimitStrategy : Strategy
{
    private const long RunningIncrement = 1L << 32;
    private const long QueuedIncrement = 1;

    // The high 32 bits track running executions and the low 32 bits track queued executions.
    // The semaphore is only a wake-up signal; permit ownership lives in _state.
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly int _queueLimit;
    private readonly long _capacity;
    private readonly Action<ConcurrencyLimitRejectedEvent>? _onRejected;
    private readonly Func<ConcurrencyLimitRejectedEvent, ValueTask>? _onRejectedAsync;
    private readonly string _telemetryName;
    private int _waiters;
    private long _pending;
    private long _state;
    private readonly KevlarMetrics.StateMetricRegistration<ConcurrencyLimitStrategy> _metricsRegistration;

    protected internal override bool InvokesContinuationAtMostOnce => true;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal int MaxConcurrency => _maxConcurrency;

    internal int QueueLimit => _queueLimit;

    internal bool HasNotification => _onRejected is not null || _onRejectedAsync is not null;

    internal (int Available, int Running, int Queued) CaptureState()
    {
        var state = Volatile.Read(ref _state);
        var running = Math.Min(_maxConcurrency, Math.Max(0, (int)(state >> 32)));
        var queued = Math.Min(_queueLimit, Math.Max(0, (int)(state & uint.MaxValue)));
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
        // Wake-ups can become redundant when a caller acquires directly before a releasing
        // execution publishes its signal. Permit ownership is validated through _state, so
        // accepting the extra signal prevents a completion from throwing SemaphoreFullException.
        _semaphore = new SemaphoreSlim(0);
        _maxConcurrency = options.MaxConcurrency;
        _queueLimit = options.QueueLimit;
        _capacity = options.MaxConcurrency + (long)options.QueueLimit;
        _onRejected = options.OnRejected;
        _onRejectedAsync = options.OnRejectedAsync;
        _telemetryName = options.Name ?? "ConcurrencyLimit";
        _metricsRegistration = KevlarMetrics.RegisterConcurrencyStateSource(this);
    }

    public override string Describe() =>
        _queueLimit > 0 ? $"ConcurrencyLimit({_maxConcurrency}, queue {_queueLimit})" : $"ConcurrencyLimit({_maxConcurrency})";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var alias = new StrategyMetricAlias(context.ShieldName, context.StrategyIndex);
        RegisterMetricsAlias(alias);
        if (!TryReserveCapacity())
        {
            return RejectAsync<T>(context);
        }

        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (TryAcquirePermit())
            {
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
        Interlocked.Add(ref _state, QueuedIncrement);
        Interlocked.Increment(ref _waiters);
        try
        {
            if (!TryAcquireQueuedPermit(drainSignal: true))
            {
                do
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
                while (!TryAcquireQueuedPermit(drainSignal: false));
            }
        }
        catch (OperationCanceledException cancelled)
        {
            Interlocked.Add(ref _state, -QueuedIncrement);
            Interlocked.Decrement(ref _pending);
            return Outcome<T>.FromException(NormalizeCancellation(cancelled, context));
        }
        finally
        {
            Interlocked.Decrement(ref _waiters);
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
        // Keep the capacity reservation until permit ownership has been published.
        ReleasePermit();
        Interlocked.Decrement(ref _pending);
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
        => TryAcquirePermitCore(isQueued: false, drainSignal: true);

    private bool TryAcquireQueuedPermit(bool drainSignal)
        => TryAcquirePermitCore(isQueued: true, drainSignal);

    private bool TryAcquirePermitCore(bool isQueued, bool drainSignal)
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            var running = (int)(state >> 32);
            if (running >= _maxConcurrency)
            {
                return false;
            }

            var updated = state + RunningIncrement - (isQueued ? QueuedIncrement : 0);
            if (Interlocked.CompareExchange(ref _state, updated, state) == state)
            {
                if (drainSignal)
                {
                    _ = _semaphore.Wait(0);
                }

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
        Interlocked.Add(ref _state, -RunningIncrement);
        if (Volatile.Read(ref _waiters) > 0)
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

using Kevlar.Internal;
using Reservoir;

namespace Kevlar.Strategies;

internal sealed class HedgingStrategy : Strategy
{
    private readonly OutcomeJudge _judge;
    private readonly int _maxAttempts;
    private readonly TimeSpan _delay;
    private readonly Func<HedgeDelayEvent, TimeSpan>? _delayGenerator;
    private readonly Func<HedgeDelayEvent, ValueTask<TimeSpan>>? _delayGeneratorAsync;
    private readonly Action<HedgeEvent>? _onHedge;
    private readonly Func<HedgeEvent, ValueTask>? _onHedgeAsync;
    private readonly HedgeActionGenerator? _actionGenerator;

    public HedgingStrategy(HedgeOptions options, OutcomeJudge judge)
        : this(options, judge, options.HasHandlingOverride)
    {
    }

    private HedgingStrategy(HedgeOptions options, OutcomeJudge judge, bool hasHandlingOverride)
    {
        Throw.IfOutOfRange(options.MaxAttempts < 1, nameof(options), "MaxAttempts must be at least 1.");
        Throw.IfOutOfRange(options.Delay < TimeSpan.Zero && options.Delay != System.Threading.Timeout.InfiniteTimeSpan, nameof(options), "Delay must be non-negative or Timeout.InfiniteTimeSpan.");
        Throw.IfOutOfRange(options.Delay > DelayHelper.MaximumDelay, nameof(options.Delay), "Delay exceeds the runtime timer limit.");

        _judge = judge;
        _maxAttempts = options.MaxAttempts;
        _delay = options.Delay;
        _delayGenerator = options.DelayGenerator;
        _delayGeneratorAsync = options.DelayGeneratorAsync;
        _onHedge = options.OnHedge;
        _onHedgeAsync = options.OnHedgeAsync;
        _actionGenerator = options.ActionGenerator;
        HasHandlingOverride = hasHandlingOverride;
    }

    internal static HedgingStrategy Create<TResult>(HedgeOptions<TResult> options, OutcomeJudge judge) =>
        new(
            options.ToUntyped(
                options.ActionGenerator is null
                    ? null
                    : HedgeActionGenerator.Create(options.ActionGenerator)),
            judge,
            options.HasHandlingOverride);

    internal void ValidateResultType(Type resultType) => _actionGenerator?.ValidateResultType(resultType);

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    internal int MaxAttempts => _maxAttempts;

    internal TimeSpan Delay => _delay;

    internal bool HasDelayGenerator => _delayGenerator is not null || _delayGeneratorAsync is not null;

    internal bool HasNotification => _onHedge is not null;

    internal bool HasActionGenerator => _actionGenerator is not null;

    protected internal override bool InvokesContinuationAtMostOnce => _maxAttempts == 1;

    public override string Describe() =>
        $"Hedge({_maxAttempts} attempts, delay {(HasDelayGenerator ? "generator" : DescribeHelper.Time(_delay))})";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (_maxAttempts == 1
            || context.Properties.SuppressAdditionalAttempts)
        {
            return next.InvokeAsync(context);
        }

        if (context.IsSynchronous)
        {
            throw new NotSupportedException("Hedging requires asynchronous execution. Use ExecuteAsync instead of Execute.");
        }

        var startedAt = HasDelayGenerator ? context.TimeProvider.GetTimestamp() : 0;
        var primary = StartPrimaryAttempt(next, context);
        if (_delay == TimeSpan.Zero || !primary.Execution.IsCompletedSuccessfully)
        {
            return ExecuteCoreAsync(next, context, primary.AsPending(), launched: 1, default, startedAt);
        }

        Outcome<T> outcome;
        try
        {
            outcome = primary.Execution.Result;
            if (!_judge.ShouldHandle(in outcome))
            {
                CopyAttemptProperties(primary.Context, context);
                return new ValueTask<Outcome<T>>(NormalizeCancellation(outcome, context));
            }
        }
        finally
        {
            primary.Dispose();
        }

        return ExecuteCoreAsync(next, context, initial: null, launched: 1, outcome, startedAt);
    }

    private async ValueTask<Outcome<T>> ExecuteCoreAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        HedgeAttempt<T>? initial,
        int launched,
        Outcome<T>? lastOutcome,
        long startedAt)
    {
        var pending = ListPool<HedgeAttempt<T>>.Shared.Rent();

        try
        {
            if (initial is { } primary)
            {
                pending.Add(primary);
            }
            else
            {
                pending.Add(await StartHedgeAttemptAsync(next, context, launched + 1, lastOutcome).ConfigureAwait(false));
                launched++;
            }

            while (true)
            {
                // Preserve fixed zero-delay parallel mode: launch all configured hedges before
                // selecting even an already-completed outcome. A generator, however, must not run
                // after an acceptable outcome has already completed.
                Task<Outcome<T>>? completed = HasDelayGenerator
                    ? FindCompletedAttempt(pending)
                    : null;
                var delay = _delay;

                if (completed is null && launched < _maxAttempts)
                {
                    delay = await GetDelayAsync(launched + 1, context, startedAt).ConfigureAwait(false);
                    if (delay == TimeSpan.Zero)
                    {
                        pending.Add(await StartHedgeAttemptAsync(next, context, launched + 1, lastOutcome).ConfigureAwait(false));
                        launched++;
                        continue;
                    }
                }

                if (completed is null
                    && launched < _maxAttempts
                    && delay != System.Threading.Timeout.InfiniteTimeSpan)
                {
                    using var delayCancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
                    var delayTask = DelayHelper.CreateDelayTask(context.TimeProvider, delay, delayCancellation.Token);
                    var winner = await WhenAnyAttemptOr(pending, delayTask).ConfigureAwait(false);

                    if (winner == delayTask)
                    {
                        pending.Add(await StartHedgeAttemptAsync(next, context, launched + 1, lastOutcome).ConfigureAwait(false));
                        launched++;
                        continue;
                    }

                    delayCancellation.Cancel();
                    completed = (Task<Outcome<T>>)winner;
                }
                else if (completed is null)
                {
                    // A single pending attempt needs no WhenAny machinery; awaiting it directly
                    // is equivalent and skips the Task[] allocation.
                    completed = pending.Count == 1
                        ? pending[0].Task
                        : await WhenAnyAttempt(pending).ConfigureAwait(false);
                }

                var outcome = NormalizeCancellation(await completed.ConfigureAwait(false), context);
                var completedAttempt = Take(pending, completed);
                try
                {
                    if (!_judge.ShouldHandle(in outcome))
                    {
                        CopyAttemptProperties(completedAttempt.Context, context);
                        return outcome;
                    }

                    lastOutcome = outcome;

                    if (launched < _maxAttempts)
                    {
                        pending.Add(await StartHedgeAttemptAsync(next, context, launched + 1, lastOutcome).ConfigureAwait(false));
                        launched++;
                    }
                    else if (pending.Count == 0)
                    {
                        CopyAttemptProperties(completedAttempt.Context, context);
                        return lastOutcome.Value;
                    }
                }
                finally
                {
                    completedAttempt.Dispose();
                }
            }
        }
        finally
        {
            foreach (var attempt in pending)
            {
                attempt.Cancellation.Cancel();
                Cleanup(attempt);
            }

            ListPool<HedgeAttempt<T>>.Shared.Return(pending);
        }
    }

    private ValueTask<TimeSpan> GetDelayAsync(
        int attemptNumber,
        KevlarContext context,
        long startedAt)
    {
        if (!HasDelayGenerator)
        {
            return new ValueTask<TimeSpan>(_delay);
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        var delayEvent = new HedgeDelayEvent(
            attemptNumber,
            context,
            context.TimeProvider.GetElapsedTime(startedAt));
        var delay = _delayGenerator is null
            ? _delay
            : NormalizeGeneratedDelay(_delayGenerator(delayEvent));

        context.CancellationToken.ThrowIfCancellationRequested();
        if (_delayGeneratorAsync is not { } delayGeneratorAsync)
        {
            return new ValueTask<TimeSpan>(delay);
        }

        var generated = delayGeneratorAsync(delayEvent);
        if (!generated.IsCompletedSuccessfully)
        {
            return AwaitGeneratedDelayAsync(generated, context);
        }

        delay = NormalizeGeneratedDelay(generated.Result);
        context.CancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<TimeSpan>(delay);
    }

    private static async ValueTask<TimeSpan> AwaitGeneratedDelayAsync(
        ValueTask<TimeSpan> generated,
        KevlarContext context)
    {
        var delay = NormalizeGeneratedDelay(await generated.ConfigureAwait(false));
        context.CancellationToken.ThrowIfCancellationRequested();
        return delay;
    }

    private static TimeSpan NormalizeGeneratedDelay(TimeSpan delay)
    {
        if (delay == System.Threading.Timeout.InfiniteTimeSpan)
        {
            return delay;
        }

        return delay < TimeSpan.Zero ? TimeSpan.Zero : DelayHelper.Clamp(delay);
    }

    private ValueTask<HedgeAttempt<T>> StartHedgeAttemptAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber,
        Outcome<T>? outcome)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var hedgeEvent = new HedgeEvent(attemptNumber, context);
        _onHedge?.Invoke(hedgeEvent);
        context.CancellationToken.ThrowIfCancellationRequested();

        if (_onHedgeAsync is { } onHedgeAsync)
        {
            var notification = onHedgeAsync(hedgeEvent);
            // Stryker disable once all: Route selection is performance-only; both branches await the hook.
            if (!notification.IsCompletedSuccessfully)
            {
                return AwaitHedgeNotificationAsync(notification, next, context, attemptNumber, outcome);
            }

            notification.GetAwaiter().GetResult();
            context.CancellationToken.ThrowIfCancellationRequested();
        }

        return new ValueTask<HedgeAttempt<T>>(
            StartHedgeAttempt(next, context, attemptNumber, outcome).AsPending());
    }

    private async ValueTask<HedgeAttempt<T>> AwaitHedgeNotificationAsync<T, TState>(
        ValueTask notification,
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber,
        Outcome<T>? outcome)
    {
        // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
        await notification.ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();
        return StartHedgeAttempt(next, context, attemptNumber, outcome).AsPending();
    }

    private StartedAttempt<T> StartPrimaryAttempt<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var cancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        try
        {
            return new StartedAttempt<T>(next.InvokeAsync(fork), cancellation, fork);
        }
        catch
        {
            KevlarContext.Return(fork);
            cancellation.Dispose();
            throw;
        }
    }

    private StartedAttempt<T> StartHedgeAttempt<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber,
        Outcome<T>? outcome)
    {
        if (_actionGenerator is not null)
        {
            return StartGeneratedHedgeAttempt(next, context, attemptNumber, outcome);
        }

        var cancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        try
        {
            KevlarMetrics.Hedge(context.ShieldName);
            return new StartedAttempt<T>(next.InvokeAsync(fork), cancellation, fork);
        }
        catch
        {
            ReleaseAttempt(fork, cancellation);
            throw;
        }
    }

    private StartedAttempt<T> StartGeneratedHedgeAttempt<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber,
        Outcome<T>? outcome)
    {
        var lifetime = HedgeAttemptLifetime.RentLinked(context.CancellationToken);
        var fork = context.Fork(lifetime.Token);
        var originalActionLease = new OriginalActionLease<T, TState>(next, lifetime);
        lifetime.Attach(fork, originalActionLease);
        try
        {
            Func<CancellationToken, ValueTask<T>>? generatedAction;
            try
            {
                generatedAction = _actionGenerator!.Generate(
                    attemptNumber,
                    fork,
                    originalActionLease.Invoke,
                    outcome);
            }
            catch (Exception exception)
            {
                KevlarMetrics.Hedge(context.ShieldName);
                return new StartedAttempt<T>(
                    new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception)),
                    lifetime,
                    fork);
            }

            context.CancellationToken.ThrowIfCancellationRequested();
            KevlarMetrics.Hedge(context.ShieldName);
            var execution = generatedAction is null
                ? next.InvokeAsync(fork)
                : InvokeGeneratedAction(generatedAction, fork.CancellationToken);
            return new StartedAttempt<T>(execution, lifetime, fork);
        }
        catch
        {
            ReleaseAttempt(fork, lifetime);
            throw;
        }
    }

    private static void ReleaseAttempt(
        KevlarContext context,
        CancellationTokenSource cancellation)
    {
        if (cancellation is HedgeAttemptLifetime lifetime)
        {
            lifetime.CompleteGeneratedAction();
            return;
        }

        KevlarContext.Return(context);
        cancellation.Dispose();
    }

    private static ValueTask<T> GetOriginalResultAsync<T>(ValueTask<Outcome<T>> execution)
    {
        // Stryker disable once all: Route selection is performance-only; both branches unwrap the outcome.
        if (execution.IsCompletedSuccessfully)
        {
            return new ValueTask<T>(execution.Result.GetResultOrRethrowInternal());
        }

        return AwaitOriginalResultAsync(execution);
    }

    private static ValueTask<T> InvokeOriginalAction<T, TState>(
        Continuation<T, TState> next,
        HedgeAttemptLifetime lifetime,
        object lease,
        CancellationToken cancellationToken)
    {
        if (!lifetime.TryRetain(lease))
        {
            throw new InvalidOperationException(
                "The original action cannot be invoked after its generated hedge action completes.");
        }

        var attemptContext = lifetime.Context;
        KevlarContext invocationContext;
        try
        {
            invocationContext = attemptContext.Fork(cancellationToken);
        }
        catch
        {
            lifetime.Release();
            throw;
        }

        ValueTask<Outcome<T>> execution;
        try
        {
            execution = next.InvokeAsync(invocationContext);
        }
        catch
        {
            KevlarContext.Return(invocationContext);
            lifetime.Release();
            throw;
        }

        if (!execution.IsCompletedSuccessfully)
        {
            return AwaitOriginalResultAsync(execution, invocationContext, lifetime);
        }

        try
        {
            return new ValueTask<T>(execution.Result.GetResultOrRethrowInternal());
        }
        finally
        {
            lifetime.CaptureCompletionProperties(invocationContext.PropertiesForCompletion);
            KevlarContext.Return(invocationContext);
            lifetime.Release();
        }
    }

    private static async ValueTask<T> AwaitOriginalResultAsync<T>(ValueTask<Outcome<T>> execution)
    {
        // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
        return (await execution.ConfigureAwait(false)).GetResultOrRethrowInternal();
    }

    private static async ValueTask<T> AwaitOriginalResultAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext invocationContext,
        HedgeAttemptLifetime lifetime)
    {
        try
        {
            // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
            return (await execution.ConfigureAwait(false)).GetResultOrRethrowInternal();
        }
        finally
        {
            lifetime.CaptureCompletionProperties(invocationContext.PropertiesForCompletion);
            KevlarContext.Return(invocationContext);
            lifetime.Release();
        }
    }

    private static ValueTask<Outcome<T>> InvokeGeneratedAction<T>(
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        ValueTask<T> execution;
        try
        {
            execution = action(cancellationToken);
        }
        catch (Exception exception)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
        }

        // Stryker disable once all: Route selection is performance-only; both branches preserve the outcome.
        if (execution.IsCompletedSuccessfully)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromResult(execution.Result));
        }

        return AwaitGeneratedActionAsync(execution);
    }

    private static async ValueTask<Outcome<T>> AwaitGeneratedActionAsync<T>(ValueTask<T> execution)
    {
        try
        {
            // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
            return Outcome<T>.FromResult(await execution.ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }
    }

    private static Task<Task> WhenAnyAttemptOr<T>(List<HedgeAttempt<T>> pending, Task delayTask)
    {
        if (pending.Count == 1)
        {
            // Common case (one in-flight attempt racing the hedge delay): the two-task
            // overload avoids the Task[] allocation.
            return Task.WhenAny(pending[0].Task, delayTask);
        }

        var tasks = new Task[pending.Count + 1];
        for (var i = 0; i < pending.Count; i++)
        {
            tasks[i] = pending[i].Task;
        }

        tasks[pending.Count] = delayTask;
        return Task.WhenAny(tasks);
    }

    private static Task<Task<Outcome<T>>> WhenAnyAttempt<T>(List<HedgeAttempt<T>> pending)
    {
        var tasks = new Task<Outcome<T>>[pending.Count];
        for (var i = 0; i < pending.Count; i++)
        {
            tasks[i] = pending[i].Task;
        }

        return Task.WhenAny(tasks);
    }

    private static Task<Outcome<T>>? FindCompletedAttempt<T>(List<HedgeAttempt<T>> pending)
    {
        for (var i = 0; i < pending.Count; i++)
        {
            if (pending[i].Task.IsCompleted)
            {
                return pending[i].Task;
            }
        }

        return null;
    }

    private static HedgeAttempt<T> Take<T>(List<HedgeAttempt<T>> pending, Task<Outcome<T>> task)
    {
        for (var i = 0; i < pending.Count; i++)
        {
            if (ReferenceEquals(pending[i].Task, task))
            {
                var attempt = pending[i];
                pending.RemoveAt(i);
                return attempt;
            }
        }

        throw new InvalidOperationException("The completed hedge attempt was not pending.");
    }

    private static void CopyAttemptProperties(KevlarContext source, KevlarContext target)
    {
        target.CaptureCompletionProperties(source.PropertiesForCompletion);
    }

    private static void Cleanup<T>(HedgeAttempt<T> attempt)
    {
        if (attempt.Task.IsCompleted)
        {
            attempt.Dispose();
            return;
        }

        _ = attempt.Task.ContinueWith(
            static (_, state) => ((HedgeAttempt<T>)state!).Dispose(),
            attempt,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static Outcome<T> NormalizeCancellation<T>(Outcome<T> outcome, KevlarContext context)
    {
        if (!context.CancellationToken.IsCancellationRequested
            || outcome.Exception is not OperationCanceledException cancellation
            || cancellation.CancellationToken == context.CancellationToken)
        {
            return outcome;
        }

        return Outcome<T>.FromException(new OperationCanceledException(
            cancellation.Message,
            cancellation,
            context.CancellationToken));
    }

    private readonly struct HedgeAttempt<T>
    {
        public HedgeAttempt(
            Task<Outcome<T>> task,
            CancellationTokenSource cancellation,
            KevlarContext context)
        {
            Task = task;
            Cancellation = cancellation;
            Context = context;
        }

        public Task<Outcome<T>> Task { get; }

        public CancellationTokenSource Cancellation { get; }

        public KevlarContext Context { get; }

        public void Dispose() => ReleaseAttempt(Context, Cancellation);
    }

    private readonly struct StartedAttempt<T>
    {
        public StartedAttempt(
            ValueTask<Outcome<T>> execution,
            CancellationTokenSource cancellation,
            KevlarContext context)
        {
            Execution = execution;
            Cancellation = cancellation;
            Context = context;
        }

        public ValueTask<Outcome<T>> Execution { get; }

        private CancellationTokenSource Cancellation { get; }

        public KevlarContext Context { get; }

        public HedgeAttempt<T> AsPending() => new(Execution.AsTask(), Cancellation, Context);

        public void Dispose() => ReleaseAttempt(Context, Cancellation);
    }

    private sealed class OriginalActionLease<T, TState>(
        Continuation<T, TState> next,
        HedgeAttemptLifetime lifetime)
    {
        public ValueTask<T> Invoke(CancellationToken cancellationToken) =>
            InvokeOriginalAction(next, lifetime, this, cancellationToken);
    }

    private sealed class HedgeAttemptLifetime : CancellationTokenSource
    {
        private static readonly ObjectPool<HedgeAttemptLifetime, PoolPolicy> Pool = new(
            maxCapacity: KevlarContext.PoolCapacity);

        private readonly KevlarProperties _initialProperties = new();
        private KevlarContext? _context;
        private object? _lease;
        private CancellationTokenRegistration _parentRegistration;
        private int _referenceCount;

        private HedgeAttemptLifetime()
        {
        }

        public KevlarContext Context => _context!;

        public static HedgeAttemptLifetime RentLinked(CancellationToken cancellationToken)
        {
            var lifetime = Pool.Rent();
            Volatile.Write(ref lifetime._referenceCount, 1);
            lifetime._parentRegistration = cancellationToken.Register(
                static state => ((HedgeAttemptLifetime)state!).Cancel(),
                lifetime);
            return lifetime;
        }

        public void Attach(KevlarContext context, object lease)
        {
            _context = context;
            _initialProperties.Clear();
            context.Properties.CopyTo(_initialProperties);
            Volatile.Write(ref _lease, lease);
        }

        public void CaptureCompletionProperties(KevlarProperties properties) =>
            Context.CaptureCompletionProperties(properties, _initialProperties);

        public bool TryRetain(object lease)
        {
            while (ReferenceEquals(Volatile.Read(ref _lease), lease))
            {
                var referenceCount = Volatile.Read(ref _referenceCount);
                if (referenceCount == 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(
                        ref _referenceCount,
                        referenceCount + 1,
                        referenceCount) == referenceCount)
                {
                    if (ReferenceEquals(Volatile.Read(ref _lease), lease))
                    {
                        return true;
                    }

                    Release();
                    return false;
                }
            }

            return false;
        }

        public void CompleteGeneratedAction()
        {
            Volatile.Write(ref _lease, null);
            Release();
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _referenceCount) != 0)
            {
                return;
            }

            var context = _context!;
            _context = null;
            Volatile.Write(ref _lease, null);
            _parentRegistration.Dispose();
            _parentRegistration = default;
            KevlarContext.Return(context);
            Pool.Return(this);
        }

        private bool Reset()
        {
#if NETSTANDARD2_0
            Dispose();
            return false;
#else
            if (TryReset())
            {
                return true;
            }

            Dispose();
            return false;
#endif
        }

        private readonly struct PoolPolicy : IPooledObjectPolicy<HedgeAttemptLifetime>
        {
            public HedgeAttemptLifetime Create() => new();

            public bool TryReset(HedgeAttemptLifetime lifetime) => lifetime.Reset();
        }
    }
}

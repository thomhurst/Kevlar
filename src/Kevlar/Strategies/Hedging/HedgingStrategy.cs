using Kevlar.Internal;
using Reservoir;

namespace Kevlar.Strategies;

internal sealed class HedgingStrategy : Strategy
{
    private readonly OutcomeJudge _judge;
    private readonly int _maxHedgedAttempts;
    private readonly TimeSpan _delay;
    private readonly Func<HedgeDelayEvent, TimeSpan>? _delayGenerator;
    private readonly Func<HedgeDelayEvent, ValueTask<TimeSpan>>? _delayGeneratorAsync;
    private readonly Action<HedgeEvent>? _onHedge;
    private readonly Func<HedgeEvent, ValueTask>? _onHedgeAsync;
    private readonly HedgeActionGenerator? _actionGenerator;
    private readonly string _telemetryName;

    public HedgingStrategy(HedgeOptions options, OutcomeJudge judge)
        : this(options, judge, options.HasHandlingOverride, options.GetType())
    {
    }

    private HedgingStrategy(
        HedgeOptions options,
        OutcomeJudge judge,
        bool hasHandlingOverride,
        Type optionsType)
    {
        ConfigurationValidation.ThrowIf(
            options.MaxHedgedAttempts < 0,
            optionsType,
            nameof(options.MaxHedgedAttempts),
            options.MaxHedgedAttempts,
            "must be non-negative");
        ConfigurationValidation.ThrowIf(
            options.Delay < TimeSpan.Zero && options.Delay != System.Threading.Timeout.InfiniteTimeSpan,
            optionsType,
            nameof(options.Delay),
            options.Delay,
            "must be non-negative or Timeout.InfiniteTimeSpan");
        ConfigurationValidation.ThrowIf(
            options.Delay > DelayHelper.MaximumDelay,
            optionsType,
            nameof(options.Delay),
            options.Delay,
            "must not exceed the runtime timer limit");

        _judge = judge;
        _maxHedgedAttempts = options.MaxHedgedAttempts;
        _delay = options.Delay;
        _delayGenerator = options.DelayGenerator;
        _delayGeneratorAsync = options.DelayGeneratorAsync;
        _onHedge = options.OnHedge;
        _onHedgeAsync = options.OnHedgeAsync;
        _actionGenerator = options.ActionGenerator;
        _telemetryName = options.Name ?? "Hedge";
        HasHandlingOverride = hasHandlingOverride;
    }

    internal static HedgingStrategy Create<TResult>(HedgeOptions<TResult> options, OutcomeJudge judge) =>
        new(
            options.ToUntyped(
                options.ActionGenerator is null
                    ? null
                    : HedgeActionGenerator.Create(options.ActionGenerator)),
            judge,
            options.HasHandlingOverride,
            options.GetType());

    internal void ValidateResultType(Type resultType) => _actionGenerator?.ValidateResultType(resultType);

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    internal int MaxHedgedAttempts => _maxHedgedAttempts;

    internal TimeSpan Delay => _delay;

    internal bool HasDelayGenerator => _delayGenerator is not null || _delayGeneratorAsync is not null;

    internal bool HasNotification => _onHedge is not null;

    internal bool HasActionGenerator => _actionGenerator is not null;

    protected internal override bool InvokesContinuationAtMostOnce => _maxHedgedAttempts == 0;

    internal override bool RequiresContinuationOverlapIsolation => false;

    public override string Describe() =>
        $"Hedge({_maxHedgedAttempts} extra, delay {(HasDelayGenerator ? "generator" : DescribeHelper.Time(_delay))})";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var strategyIndex = context.StrategyIndex;
        if (_maxHedgedAttempts == 0
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
            return ExecuteCoreAsync(
                next,
                context,
                primary.AsPending(),
                hedgesLaunched: 0,
                default,
                startedAt,
                strategyIndex);
        }

        Outcome<T> outcome;
        bool shouldHandle;
        try
        {
            outcome = primary.Execution.Result;
            shouldHandle = _judge.ShouldHandle(
                in outcome,
                primary.Context,
                attempt: 0,
                strategyIndex);
            if (!shouldHandle)
            {
                CopyAttemptProperties(primary.Context, context);
            }
        }
        finally
        {
            primary.Dispose();
        }

        if (!shouldHandle)
        {
            return new ValueTask<Outcome<T>>(NormalizeCancellation(outcome, context));
        }

        return ExecuteCoreAsync(
            next,
            context,
            initial: null,
            hedgesLaunched: 0,
            outcome,
            startedAt,
            strategyIndex);
    }

    private async ValueTask<Outcome<T>> ExecuteCoreAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        HedgeAttempt<T>? initial,
        int hedgesLaunched,
        Outcome<T>? lastOutcome,
        long startedAt,
        int strategyIndex)
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
                pending.Add(await StartHedgeAttemptAsync(next, context, 2, lastOutcome).ConfigureAwait(false));
                hedgesLaunched++;
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

                if (completed is null && hedgesLaunched < _maxHedgedAttempts)
                {
                    delay = await GetDelayAsync(hedgesLaunched + 2, context, startedAt).ConfigureAwait(false);
                    if (delay == TimeSpan.Zero)
                    {
                        pending.Add(await StartHedgeAttemptAsync(next, context, hedgesLaunched + 2, lastOutcome).ConfigureAwait(false));
                        hedgesLaunched++;
                        continue;
                    }
                }

                if (completed is null
                    && hedgesLaunched < _maxHedgedAttempts
                    && delay != System.Threading.Timeout.InfiniteTimeSpan)
                {
                    using var delayCancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
                    var delayTask = DelayHelper.CreateDelayTask(context.TimeProvider, delay, delayCancellation.Token);
                    var winner = await WhenAnyAttemptOr(pending, delayTask).ConfigureAwait(false);

                    if (winner == delayTask)
                    {
                        pending.Add(await StartHedgeAttemptAsync(next, context, hedgesLaunched + 2, lastOutcome).ConfigureAwait(false));
                        hedgesLaunched++;
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
                var completedAttempt = Remove(pending, completed);
                bool shouldHandle;
                try
                {
                    var judgingContext = completedAttempt.FreezeContext(in outcome);
                    shouldHandle = _judge.ShouldHandle(
                        in outcome,
                        judgingContext,
                        completedAttempt.Attempt,
                        strategyIndex);
                    if (!shouldHandle
                        || hedgesLaunched == _maxHedgedAttempts && pending.Count == 0)
                    {
                        if (!ReferenceEquals(judgingContext, completedAttempt.Context))
                        {
                            CopyAttemptProperties(judgingContext, completedAttempt.Context);
                        }

                        CopyAttemptProperties(completedAttempt.Context, context);
                    }
                }
                finally
                {
                    completedAttempt.Dispose();
                }

                if (!shouldHandle)
                {
                    return outcome;
                }

                lastOutcome = outcome;

                if (hedgesLaunched < _maxHedgedAttempts)
                {
                    pending.Add(await StartHedgeAttemptAsync(next, context, hedgesLaunched + 2, lastOutcome).ConfigureAwait(false));
                    hedgesLaunched++;
                }
                else if (pending.Count == 0)
                {
                    return lastOutcome!.Value;
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
        CallbackInvoker.Invoke(_onHedge, hedgeEvent, CallbackErrorKind.Hedge, context);
        context.CancellationToken.ThrowIfCancellationRequested();

        var notification = CallbackInvoker.InvokeAsync(
            _onHedgeAsync,
            hedgeEvent,
            CallbackErrorKind.Hedge,
            context);
        if (!notification.IsCompletedSuccessfully)
        {
            return AwaitHedgeNotificationAsync(notification, next, context, attemptNumber, outcome);
        }

        context.CancellationToken.ThrowIfCancellationRequested();
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
            return new StartedAttempt<T>(next.InvokeAsync(fork), cancellation, fork, attempt: 0);
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
        var cancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        OriginalActionContextCapture<T>? contextCapture = null;
        try
        {
            Func<CancellationToken, ValueTask<T>>? generatedAction = null;
            if (_actionGenerator is not null)
            {
                contextCapture = OriginalActionContextCapture<T>.Rent(
                    fork,
                    cancellation,
                    out var captureVersion);
                Func<CancellationToken, ValueTask<T>> originalAction =
                    token => InvokeOriginalAction(next, contextCapture, captureVersion, token);
                try
                {
                    generatedAction = _actionGenerator.Generate(attemptNumber, fork, originalAction, outcome);
                }
                catch (Exception exception)
                {
                    KevlarMetrics.Hedge(context, _telemetryName, attemptNumber - 1);
                    return new StartedAttempt<T>(
                        new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception)),
                        cancellation,
                        fork,
                        attemptNumber - 1,
                        contextCapture);
                }

                context.CancellationToken.ThrowIfCancellationRequested();
            }

            KevlarMetrics.Hedge(context, _telemetryName, attemptNumber - 1);
            var execution = generatedAction is null
                ? next.InvokeAsync(fork)
                : InvokeGeneratedAction(generatedAction, fork.CancellationToken);
            return new StartedAttempt<T>(execution, cancellation, fork, attemptNumber - 1, contextCapture);
        }
        catch
        {
            ReleaseAttemptResources(fork, cancellation, contextCapture);
            throw;
        }
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
        OriginalActionContextCapture<T> contextCapture,
        int captureVersion,
        CancellationToken cancellationToken)
    {
        var invocationContext = contextCapture.Fork(captureVersion, cancellationToken);
        ValueTask<Outcome<T>> execution;
        try
        {
            execution = next.InvokeAsync(invocationContext);
        }
        catch (Exception exception)
        {
            var failedOutcome = Outcome<T>.FromException(exception);
            CaptureAndReturn(contextCapture, invocationContext, in failedOutcome);
            throw;
        }

        if (!execution.IsCompletedSuccessfully)
        {
            return AwaitOriginalResultAsync(execution, invocationContext, contextCapture);
        }

        var outcome = execution.Result;
        try
        {
            return new ValueTask<T>(outcome.GetResultOrRethrowInternal());
        }
        finally
        {
            CaptureAndReturn(contextCapture, invocationContext, in outcome);
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
        OriginalActionContextCapture<T> contextCapture)
    {
        Outcome<T> outcome;
        try
        {
            // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
            outcome = await execution.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            outcome = Outcome<T>.FromException(exception);
            CaptureAndReturn(contextCapture, invocationContext, in outcome);
            throw;
        }

        try
        {
            return outcome.GetResultOrRethrowInternal();
        }
        finally
        {
            CaptureAndReturn(contextCapture, invocationContext, in outcome);
        }
    }

    private static void CaptureAndReturn<T>(
        OriginalActionContextCapture<T> contextCapture,
        KevlarContext invocationContext,
        in Outcome<T> outcome)
    {
        var retained = false;
        try
        {
            retained = contextCapture.Capture(invocationContext, in outcome);
        }
        finally
        {
            try
            {
                if (!retained)
                {
                    KevlarContext.Return(invocationContext);
                }
            }
            finally
            {
                contextCapture.ReleaseInvocation();
            }
        }
    }

    private static void ReleaseAttemptResources<T>(
        KevlarContext context,
        CancellationTokenSource cancellation,
        OriginalActionContextCapture<T>? contextCapture)
    {
        if (contextCapture is not null)
        {
            contextCapture.ReleaseAttempt();
            return;
        }

        try
        {
            KevlarContext.Return(context);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private sealed class OriginalActionContextCapture<T>
    {
        private static readonly ObjectPool<OriginalActionContextCapture<T>, PoolPolicy> Pool = new(
            maxCapacity: KevlarContext.PoolCapacity);

        private readonly object _sync = new();
        private readonly KevlarProperties _initialProperties = new();
        private readonly KevlarProperties _mergedProperties = new();
        private KevlarContext? _context;
        private CancellationTokenSource? _cancellation;
        private List<CapturedOriginalAction>? _completions;
        private KevlarContext? _selectedContext;
        private bool _acceptingInvocations;
        private bool _frozen;
        private int _references;
        private int _version;

        private OriginalActionContextCapture()
        {
        }

        public static OriginalActionContextCapture<T> Rent(
            KevlarContext context,
            CancellationTokenSource cancellation,
            out int version)
        {
            var capture = Pool.Rent();
            lock (capture._sync)
            {
                capture._context = context;
                capture._cancellation = cancellation;
                capture._completions = null;
                capture._selectedContext = null;
                capture._initialProperties.Clear();
                context.Properties.CopyTo(capture._initialProperties);
                capture._acceptingInvocations = true;
                capture._frozen = false;
                capture._references = 1;
                capture._version++;
                version = capture._version;
            }

            return capture;
        }

        public KevlarContext Fork(int version, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_acceptingInvocations || _version != version)
                {
                    throw new ObjectDisposedException(nameof(OriginalActionContextCapture<T>));
                }

                _references++;
                try
                {
                    return _context!.Fork(cancellationToken);
                }
                catch
                {
                    _references--;
                    throw;
                }
            }
        }

        public bool Capture(KevlarContext source, in Outcome<T> outcome)
        {
            lock (_sync)
            {
                if (_frozen)
                {
                    return false;
                }

                _completions ??= [];
                _completions.Add(new CapturedOriginalAction(source, outcome));
                return true;
            }
        }

        public void ReleaseInvocation() => ReleaseReference(stopAcceptingInvocations: false);

        public KevlarContext Freeze(in Outcome<T> selectedOutcome)
        {
            List<CapturedOriginalAction>? completions = null;
            try
            {
                lock (_sync)
                {
                    _frozen = true;
                    completions = _completions;
                    _completions = null;
                    if (completions is null)
                    {
                        return _context!;
                    }

                    var selectedIndex = FindSelectedIndex(completions, in selectedOutcome);

                    var selected = completions[selectedIndex];
                    completions.RemoveAt(selectedIndex);
                    _selectedContext = selected.Context;
                    MergeContext(selected.Context, _context!);
                    return selected.Context;
                }
            }
            finally
            {
                ReturnContexts(completions);
            }
        }

        private static int FindSelectedIndex(
            List<CapturedOriginalAction> completions,
            in Outcome<T> selectedOutcome)
        {
            if (selectedOutcome.IsSuccess && !typeof(T).IsValueType)
            {
                for (var i = completions.Count - 1; i >= 0; i--)
                {
                    var candidate = completions[i].Outcome;
                    if (candidate.IsSuccess
                        && ReferenceEquals(candidate.Result, selectedOutcome.Result))
                    {
                        return i;
                    }
                }
            }

            for (var i = completions.Count - 1; i >= 0; i--)
            {
                if (Matches(completions[i].Outcome, selectedOutcome))
                {
                    return i;
                }
            }

            return completions.Count - 1;
        }

        public void ReleaseAttempt() => ReleaseReference(stopAcceptingInvocations: true);

        private void ReleaseReference(bool stopAcceptingInvocations)
        {
            KevlarContext? context = null;
            KevlarContext? selectedContext = null;
            CancellationTokenSource? cancellation = null;
            List<CapturedOriginalAction>? completions = null;
            lock (_sync)
            {
                if (stopAcceptingInvocations)
                {
                    _acceptingInvocations = false;
                    _frozen = true;
                    completions = _completions;
                    _completions = null;
                    selectedContext = _selectedContext;
                    _selectedContext = null;
                }

                _references--;
                if (_references == 0)
                {
                    context = _context;
                    cancellation = _cancellation;
                    _context = null;
                    _cancellation = null;
                }
            }

            ReturnContexts(completions);
            if (selectedContext is not null)
            {
                KevlarContext.Return(selectedContext);
            }

            if (context is null)
            {
                return;
            }

            try
            {
                KevlarContext.Return(context);
            }
            finally
            {
                cancellation!.Dispose();
                Pool.Return(this);
            }
        }

        private static bool Matches(Outcome<T> candidate, Outcome<T> selected)
        {
            if (candidate.IsSuccess != selected.IsSuccess)
            {
                return false;
            }

            if (!candidate.IsSuccess)
            {
                return ReferenceEquals(candidate.Exception, selected.Exception);
            }

            try
            {
                return EqualityComparer<T>.Default.Equals(candidate.Result!, selected.Result!);
            }
            catch
            {
                return false;
            }
        }

        private void MergeContext(KevlarContext source, KevlarContext target)
        {
            _mergedProperties.Clear();
            source.PropertiesForCompletion.CopyTo(_mergedProperties);
            target.Properties.ApplyChangesSince(_initialProperties, _mergedProperties);

            source.Properties.MirrorMutationsTo(null);
            source.Properties.Clear();
            _mergedProperties.CopyTo(source.Properties);
            source.CaptureCompletionProperties(source.Properties);
            source.CopyCompletionPropertiesTo(target);
        }

        private static void ReturnContexts(List<CapturedOriginalAction>? completions)
        {
            if (completions is null)
            {
                return;
            }

            foreach (var completion in completions)
            {
                KevlarContext.Return(completion.Context);
            }
        }

        private readonly record struct CapturedOriginalAction(
            KevlarContext Context,
            Outcome<T> Outcome);

        private readonly struct PoolPolicy : IPooledObjectPolicy<OriginalActionContextCapture<T>>
        {
            public OriginalActionContextCapture<T> Create() => new();

            public bool TryReset(OriginalActionContextCapture<T> capture) => true;
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

    private static HedgeAttempt<T> Remove<T>(List<HedgeAttempt<T>> pending, Task<Outcome<T>> task)
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
            KevlarContext context,
            int attempt,
            OriginalActionContextCapture<T>? contextCapture)
        {
            Task = task;
            Cancellation = cancellation;
            Context = context;
            Attempt = attempt;
            ContextCapture = contextCapture;
        }

        public Task<Outcome<T>> Task { get; }

        public CancellationTokenSource Cancellation { get; }

        public KevlarContext Context { get; }

        public int Attempt { get; }

        private OriginalActionContextCapture<T>? ContextCapture { get; }

        public KevlarContext FreezeContext(in Outcome<T> outcome) =>
            ContextCapture?.Freeze(in outcome) ?? Context;

        public void Dispose() => ReleaseAttemptResources(Context, Cancellation, ContextCapture);
    }

    private static void CopyAttemptProperties(KevlarContext source, KevlarContext target) =>
        source.CopyCompletionPropertiesToParent(target);

    private readonly struct StartedAttempt<T>
    {
        public StartedAttempt(
            ValueTask<Outcome<T>> execution,
            CancellationTokenSource cancellation,
            KevlarContext context,
            int attempt,
            OriginalActionContextCapture<T>? contextCapture = null)
        {
            Execution = execution;
            Cancellation = cancellation;
            Context = context;
            Attempt = attempt;
            ContextCapture = contextCapture;
        }

        public ValueTask<Outcome<T>> Execution { get; }

        private CancellationTokenSource Cancellation { get; }

        public KevlarContext Context { get; }

        private int Attempt { get; }

        private OriginalActionContextCapture<T>? ContextCapture { get; }

        public HedgeAttempt<T> AsPending() => new(Execution.AsTask(), Cancellation, Context, Attempt, ContextCapture);

        public void Dispose() => ReleaseAttemptResources(Context, Cancellation, ContextCapture);
    }
}

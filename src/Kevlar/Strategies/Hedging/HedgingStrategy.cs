using Kevlar.Internal;
using Reservoir;

namespace Kevlar.Strategies;

internal sealed class HedgingStrategy : Strategy
{
    private const long DisabledAttemptTimestamp = long.MinValue;

    protected internal override string? SynchronousExecutionUnsupportedReason =>
        _maxHedgedAttempts > 0 ? "hedging" : null;
    private readonly OutcomeJudge _judge;
    private readonly int _maxHedgedAttempts;
    private readonly TimeSpan _delay;
    private readonly Func<HedgeDelayEvent, ValueTask<TimeSpan>>? _delayGenerator;
    private readonly Delegate? _onHedge;
    private readonly HedgeActionGeneratorAdapter? _actionGenerator;
    private readonly Type? _callbackResultType;
    private readonly string _telemetryName;
    private readonly string _onHedgeHookName;

    public HedgingStrategy(HedgeOptions options, OutcomeJudge judge)
        : this(
            options,
            judge,
            options.HasHandlingOverride,
            options.GetType(),
            options.OnHedge,
            options.ActionGenerator is null
                ? null
                : HedgeActionGeneratorAdapter.Create(options.ActionGenerator),
            callbackResultType: null)
    {
    }

    private HedgingStrategy(
        HedgeOptions options,
        OutcomeJudge judge,
        bool hasHandlingOverride,
        Type optionsType,
        Delegate? onHedge,
        HedgeActionGeneratorAdapter? actionGenerator,
        Type? callbackResultType)
    {
        ConfigurationValidation.ThrowIf(
            options.MaxHedgedAttempts < 0,
            optionsType,
            nameof(options.MaxHedgedAttempts),
            options.MaxHedgedAttempts,
            "must be non-negative");
        ConfigurationValidation.ThrowIf(
            options.Delay > DelayHelper.MaximumDelay,
            optionsType,
            nameof(options.Delay),
            options.Delay,
            "must not exceed the runtime timer limit");

        _judge = judge;
        _maxHedgedAttempts = options.MaxHedgedAttempts;
        _delay = options.Delay < TimeSpan.Zero
            ? System.Threading.Timeout.InfiniteTimeSpan
            : options.Delay;
        _delayGenerator = options.DelayGenerator;
        _onHedge = onHedge;
        _actionGenerator = actionGenerator;
        _callbackResultType = callbackResultType;
        _telemetryName = options.Name ?? "Hedge";
        _onHedgeHookName = callbackResultType is null
            ? "HedgeOptions.OnHedge"
            : "HedgeOptions<TResult>.OnHedge";
        HasHandlingOverride = hasHandlingOverride;
    }

    internal static HedgingStrategy Create<TResult>(HedgeOptions<TResult> options, OutcomeJudge judge)
    {
        var untyped = options.ToUntyped();
        return new(
            untyped,
            judge,
            options.HasHandlingOverride,
            options.GetType(),
            options.OnHedge,
            options.ActionGenerator is null
                ? null
                : HedgeActionGeneratorAdapter.Create(options.ActionGenerator),
            typeof(TResult));
    }

    internal void ValidateResultType(Type resultType)
    {
        _actionGenerator?.ValidateResultType(resultType);
        if (_callbackResultType is not null && _callbackResultType != resultType)
        {
            throw new InvalidOperationException(
                $"The hedge callback was created for '{_callbackResultType}', " +
                $"but this shield returns '{resultType}'.");
        }
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    internal int MaxHedgedAttempts => _maxHedgedAttempts;

    internal TimeSpan Delay => _delay;

    internal bool HasDelayGenerator => _delayGenerator is not null;

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
        var primaryStartedAt = GetAttemptStartedAt(context);
        var primary = StartPrimaryAttempt(next, context);
        if (_delay == TimeSpan.Zero || !primary.Execution.IsCompletedSuccessfully)
        {
            return ExecuteCoreAsync(
                next,
                context,
                primary.AsPending(primaryStartedAt),
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
            var suppressAdditionalAttempts = PropagateAttemptSuppression(primary.Context, context);
            shouldHandle = _judge.ShouldHandle(
                in outcome,
                primary.Context,
                attempt: 0,
                strategyIndex);
            RecordAttempt(
                primary.Context,
                primary.Attempt,
                primaryStartedAt,
                in outcome,
                isWinner: !shouldHandle || suppressAdditionalAttempts,
                _telemetryName);

            if (!shouldHandle || suppressAdditionalAttempts)
            {
                CopyAttemptProperties(primary.Context, context);
            }
        }
        finally
        {
            primary.Dispose();
        }

        if (!shouldHandle || context.Properties.SuppressAdditionalAttempts)
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
        Outcome<T>? terminalOutcome = null;

        try
        {
            if (initial is { } primary)
            {
                pending.Add(primary);
            }
            else
            {
                pending.Add(await StartHedgeAttemptAsync(
                    next,
                    context,
                    1,
                    lastOutcome,
                    TimeSpan.Zero).ConfigureAwait(false));
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
                    delay = await GetDelayAsync(hedgesLaunched + 1, context, startedAt).ConfigureAwait(false);
                    if (delay == TimeSpan.Zero)
                    {
                        pending.Add(await StartHedgeAttemptAsync(
                            next,
                            context,
                            hedgesLaunched + 1,
                            lastOutcome,
                            TimeSpan.Zero).ConfigureAwait(false));
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
                        pending.Add(await StartHedgeAttemptAsync(
                            next,
                            context,
                            hedgesLaunched + 1,
                            lastOutcome,
                            delay).ConfigureAwait(false));
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
                    var judgingContext = await completedAttempt
                        .FreezeContextAsync(in outcome)
                        .ConfigureAwait(false);
                    shouldHandle = _judge.ShouldHandle(
                        in outcome,
                        judgingContext,
                        completedAttempt.Attempt,
                        strategyIndex);
                    var suppressAdditionalAttempts = PropagateAttemptSuppression(
                        judgingContext,
                        context,
                        pending);
                    var isWinner = !shouldHandle
                        || suppressAdditionalAttempts && pending.Count == 0
                        || hedgesLaunched == _maxHedgedAttempts && pending.Count == 0;
                    RecordAttempt(
                        in completedAttempt,
                        in outcome,
                        isWinner,
                        _telemetryName);
                    if (isWinner)
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
                    await completedAttempt.DisposeAsync().ConfigureAwait(false);
                }

                if (!shouldHandle
                    || context.Properties.SuppressAdditionalAttempts && pending.Count == 0)
                {
                    if (lastOutcome is { } superseded)
                    {
                        if (!OutcomeDisposer.IsSameResult(in superseded, in outcome))
                        {
                            await OutcomeDisposer.DisposeResultAsync(
                                in superseded,
                                context).ConfigureAwait(false);
                        }

                        lastOutcome = null;
                    }

                    terminalOutcome = outcome;
                    return outcome;
                }

                if (lastOutcome is { } previous)
                {
                    await OutcomeDisposer.DisposeResultAsync(in previous, context).ConfigureAwait(false);
                }

                lastOutcome = outcome;

                if (!context.Properties.SuppressAdditionalAttempts
                    && hedgesLaunched < _maxHedgedAttempts)
                {
                    pending.Add(await StartHedgeAttemptAsync(
                        next,
                        context,
                        hedgesLaunched + 1,
                        lastOutcome,
                        TimeSpan.Zero).ConfigureAwait(false));
                    hedgesLaunched++;
                }
                else if (pending.Count == 0)
                {
                    var terminal = lastOutcome!.Value;
                    lastOutcome = null;
                    terminalOutcome = terminal;
                    return terminal;
                }
            }
        }
        finally
        {
            foreach (var attempt in pending)
            {
                attempt.Cancellation.Cancel();
                Cleanup(attempt, terminalOutcome, _telemetryName);
            }

            if (lastOutcome is { } superseded)
            {
                await OutcomeDisposer.DisposeResultAsync(in superseded, context).ConfigureAwait(false);
            }

            ListPool<HedgeAttempt<T>>.Shared.Return(pending);
        }
    }

    private ValueTask<TimeSpan> GetDelayAsync(
        int attemptNumber,
        KevlarContext context,
        long startedAt)
    {
        if (_delayGenerator is not { } delayGenerator)
        {
            return new ValueTask<TimeSpan>(_delay);
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        var delayEvent = new HedgeDelayEvent(
            attemptNumber,
            context,
            context.TimeProvider.GetElapsedTime(startedAt));
        var generated = CallbackInvoker.InvokeGenerator(
            delayGenerator,
            delayEvent,
            context,
            "HedgeOptions.DelayGenerator");
        if (!generated.IsCompletedSuccessfully)
        {
            return AwaitGeneratedDelayAsync(generated, context);
        }

        var delay = NormalizeGeneratedDelay(generated.Result);
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
        Outcome<T>? outcome,
        TimeSpan delay)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var notification = _onHedge is null
            ? default
            : InvokeOnHedgeAsync(_onHedge, attemptNumber, outcome, context);
        if (!notification.IsCompletedSuccessfully)
        {
            return AwaitHedgeNotificationAsync(
                notification,
                next,
                context,
                attemptNumber,
                outcome,
                delay);
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<HedgeAttempt<T>>(
            StartHedgeAttempt(next, context, attemptNumber, outcome, delay));
    }

    private ValueTask InvokeOnHedgeAsync<T>(
        Delegate callback,
        int attemptNumber,
        Outcome<T>? outcome,
        KevlarContext context)
    {
        if (_callbackResultType is null)
        {
            return CallbackInvoker.InvokeAsync(
                (Func<HedgeEvent, ValueTask>)callback,
                new HedgeEvent(attemptNumber, context),
                CallbackErrorKind.Hedge,
                context,
                _onHedgeHookName);
        }

        return CallbackInvoker.InvokeAsync(
            (Func<HedgeEvent<T>, ValueTask>)callback,
            new HedgeEvent<T>(attemptNumber, outcome, context),
            CallbackErrorKind.Hedge,
            context,
            _onHedgeHookName);
    }

    private async ValueTask<HedgeAttempt<T>> AwaitHedgeNotificationAsync<T, TState>(
        ValueTask notification,
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber,
        Outcome<T>? outcome,
        TimeSpan delay)
    {
        // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
        await notification.ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();
        return StartHedgeAttempt(next, context, attemptNumber, outcome, delay);
    }

    private StartedAttempt<T> StartPrimaryAttempt<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var cancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        fork.AttemptNumber = 0;
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

    private HedgeAttempt<T> StartHedgeAttempt<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        int attemptNumber,
        Outcome<T>? outcome,
        TimeSpan delay)
    {
        var cancellation = CancellationTokenSourcePool.Shared.RentLinked(context.CancellationToken);
        var fork = context.Fork(cancellation.Token);
        fork.AttemptNumber = attemptNumber;
        var startedAt = GetAttemptStartedAt(fork);
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
                    KevlarMetrics.Hedge(
                        context,
                        _telemetryName,
                        attemptNumber,
                        exception,
                        delay);
                    return new HedgeAttempt<T>(
                        Task.FromResult(Outcome<T>.FromException(exception)),
                        cancellation,
                        fork,
                        attemptNumber,
                        startedAt,
                        contextCapture);
                }

                context.CancellationToken.ThrowIfCancellationRequested();
            }

            KevlarMetrics.Hedge(
                context,
                _telemetryName,
                attemptNumber,
                delay: delay);
            var execution = generatedAction is null
                ? next.InvokeAsync(fork)
                : InvokeGeneratedAction(generatedAction, fork.CancellationToken);
            return new HedgeAttempt<T>(
                execution.AsTask(),
                cancellation,
                fork,
                attemptNumber,
                startedAt,
                contextCapture);
        }
        catch
        {
            var release = ReleaseAttemptResourcesAsync(fork, cancellation, contextCapture);
            if (!release.IsCompletedSuccessfully)
            {
                _ = release.AsTask();
            }

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
        if (contextCapture.Capture(invocationContext, in outcome))
        {
            contextCapture.ReleaseInvocation();
            return;
        }

        var disposal = OutcomeDisposer.DisposeResultAsync(in outcome, invocationContext);
        if (disposal.IsCompletedSuccessfully)
        {
            try
            {
                disposal.GetAwaiter().GetResult();
                KevlarContext.Return(invocationContext);
            }
            finally
            {
                contextCapture.ReleaseInvocation();
            }

            return;
        }

        _ = DisposeAndReleaseInvocationAsync(disposal, invocationContext, contextCapture);
    }

    private static async Task DisposeAndReleaseInvocationAsync<T>(
        ValueTask disposal,
        KevlarContext invocationContext,
        OriginalActionContextCapture<T> contextCapture)
    {
        try
        {
            await disposal.ConfigureAwait(false);
            KevlarContext.Return(invocationContext);
        }
        finally
        {
            contextCapture.ReleaseInvocation();
        }
    }

    private static ValueTask ReleaseAttemptResourcesAsync<T>(
        KevlarContext context,
        CancellationTokenSource cancellation,
        OriginalActionContextCapture<T>? contextCapture)
    {
        if (contextCapture is not null)
        {
            return contextCapture.ReleaseAttemptAsync();
        }

        try
        {
            KevlarContext.Return(context);
        }
        finally
        {
            cancellation.Dispose();
        }

        return default;
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

        public void ReleaseInvocation() => ReleaseReference();

        public ValueTask<KevlarContext> FreezeAsync(in Outcome<T> selectedOutcome)
        {
            List<CapturedOriginalAction>? completions = null;
            CapturedOriginalAction? selected = null;
            lock (_sync)
            {
                _frozen = true;
                completions = _completions;
                _completions = null;
                if (completions is null)
                {
                    return new ValueTask<KevlarContext>(_context!);
                }

                var selectedIndex = FindSelectedIndex(completions, in selectedOutcome);
                selected = completions[selectedIndex];
                completions.RemoveAt(selectedIndex);
                _selectedContext = selected.Value.Context;
                MergeContext(selected.Value.Context, _context!);
            }

            return CompleteFreezeAsync(selected.Value, selectedOutcome, completions);
        }

        private static async ValueTask<KevlarContext> CompleteFreezeAsync(
            CapturedOriginalAction selected,
            Outcome<T> selectedOutcome,
            List<CapturedOriginalAction> completions)
        {
            var selectedOriginalOutcome = selected.Outcome;
            if (!OutcomeDisposer.IsSameResult(in selectedOriginalOutcome, in selectedOutcome))
            {
                await OutcomeDisposer.DisposeResultAsync(
                    in selectedOriginalOutcome,
                    selected.Context).ConfigureAwait(false);
            }

            await DisposeAndReturnContextsAsync(completions).ConfigureAwait(false);
            return selected.Context;
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

        public ValueTask ReleaseAttemptAsync()
        {
            KevlarContext? context = null;
            KevlarContext? selectedContext;
            CancellationTokenSource? cancellation = null;
            List<CapturedOriginalAction>? completions;
            lock (_sync)
            {
                _acceptingInvocations = false;
                _frozen = true;
                completions = _completions;
                _completions = null;
                selectedContext = _selectedContext;
                _selectedContext = null;

                _references--;
                if (_references == 0)
                {
                    context = _context;
                    cancellation = _cancellation;
                    _context = null;
                    _cancellation = null;
                }
            }

            return ReleaseAttemptCoreAsync(
                completions,
                selectedContext,
                context,
                cancellation);
        }

        private async ValueTask ReleaseAttemptCoreAsync(
            List<CapturedOriginalAction>? completions,
            KevlarContext? selectedContext,
            KevlarContext? context,
            CancellationTokenSource? cancellation)
        {
            if (completions is not null)
            {
                await DisposeAndReturnContextsAsync(completions).ConfigureAwait(false);
            }

            if (selectedContext is not null)
            {
                KevlarContext.Return(selectedContext);
            }

            if (context is not null)
            {
                ReturnCapture(context, cancellation!);
            }
        }

        private void ReleaseReference()
        {
            KevlarContext? context = null;
            CancellationTokenSource? cancellation = null;
            lock (_sync)
            {
                _references--;
                if (_references == 0)
                {
                    context = _context;
                    cancellation = _cancellation;
                    _context = null;
                    _cancellation = null;
                }
            }

            if (context is null)
            {
                return;
            }

            ReturnCapture(context, cancellation!);
        }

        private void ReturnCapture(
            KevlarContext context,
            CancellationTokenSource cancellation)
        {
            try
            {
                KevlarContext.Return(context);
            }
            finally
            {
                cancellation.Dispose();
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

        private static async ValueTask DisposeAndReturnContextsAsync(
            List<CapturedOriginalAction> completions)
        {
            foreach (var completion in completions)
            {
                try
                {
                    var outcome = completion.Outcome;
                    await OutcomeDisposer.DisposeResultAsync(
                        in outcome,
                        completion.Context).ConfigureAwait(false);
                }
                finally
                {
                    KevlarContext.Return(completion.Context);
                }
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

    private static void Cleanup<T>(
        HedgeAttempt<T> attempt,
        Outcome<T>? terminalOutcome,
        string strategyName)
    {
        if (attempt.Task.Status != TaskStatus.RanToCompletion)
        {
            _ = CleanupAsync(attempt, terminalOutcome, strategyName);
            return;
        }

        var outcome = attempt.Task.Result;
        RecordAttempt(in attempt, in outcome, isWinner: false, strategyName);
        var disposal = terminalOutcome is { } terminal
            && OutcomeDisposer.IsSameResult(in outcome, in terminal)
                ? default
                : OutcomeDisposer.DisposeResultAsync(in outcome, attempt.Context);
        if (disposal.IsCompletedSuccessfully)
        {
            disposal.GetAwaiter().GetResult();
            var release = attempt.DisposeAsync();
            if (!release.IsCompletedSuccessfully)
            {
                _ = release.AsTask();
            }
            return;
        }

        _ = FinishCleanupAsync(disposal, attempt);
    }

    private static async Task CleanupAsync<T>(
        HedgeAttempt<T> attempt,
        Outcome<T>? terminalOutcome,
        string strategyName)
    {
        var recorded = false;
        try
        {
            var outcome = await attempt.Task.ConfigureAwait(false);
            recorded = true;
            RecordAttempt(in attempt, in outcome, isWinner: false, strategyName);
            if (terminalOutcome is not { } terminal
                || !OutcomeDisposer.IsSameResult(in outcome, in terminal))
            {
                await OutcomeDisposer.DisposeResultAsync(
                    in outcome,
                    attempt.Context).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            if (!recorded)
            {
                var outcome = Outcome<T>.FromException(exception);
                RecordAttempt(in attempt, in outcome, isWinner: false, strategyName);
            }

            // Attempt failures are already represented by the selected pipeline outcome.
        }
        finally
        {
            await attempt.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task FinishCleanupAsync<T>(
        ValueTask disposal,
        HedgeAttempt<T> attempt)
    {
        try
        {
            await disposal.ConfigureAwait(false);
        }
        finally
        {
            await attempt.DisposeAsync().ConfigureAwait(false);
        }
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

    private static long GetAttemptStartedAt(KevlarContext context) =>
        KevlarMetrics.HedgeAttemptEnabled(context)
            ? context.TimeProvider.GetTimestamp()
            : DisabledAttemptTimestamp;

    private static void RecordAttempt<T>(
        in HedgeAttempt<T> attempt,
        in Outcome<T> outcome,
        bool isWinner,
        string strategyName) =>
        RecordAttempt(
            attempt.Context,
            attempt.Attempt,
            attempt.StartedAt,
            in outcome,
            isWinner,
            strategyName);

    private static void RecordAttempt<T>(
        KevlarContext context,
        int attemptNumber,
        long startedAt,
        in Outcome<T> outcome,
        bool isWinner,
        string strategyName)
    {
        if (startedAt == DisabledAttemptTimestamp)
        {
            return;
        }

        KevlarMetrics.HedgeAttempt(
            context,
            strategyName,
            attemptNumber,
            in outcome,
            isWinner,
            context.TimeProvider.GetElapsedTime(startedAt));
    }

    private readonly struct HedgeAttempt<T>
    {
        public HedgeAttempt(
            Task<Outcome<T>> task,
            CancellationTokenSource cancellation,
            KevlarContext context,
            int attempt,
            long startedAt,
            OriginalActionContextCapture<T>? contextCapture)
        {
            Task = task;
            Cancellation = cancellation;
            Context = context;
            Attempt = attempt;
            StartedAt = startedAt;
            ContextCapture = contextCapture;
        }

        public Task<Outcome<T>> Task { get; }

        public CancellationTokenSource Cancellation { get; }

        public KevlarContext Context { get; }

        public int Attempt { get; }

        public long StartedAt { get; }

        private OriginalActionContextCapture<T>? ContextCapture { get; }

        public ValueTask<KevlarContext> FreezeContextAsync(in Outcome<T> outcome) =>
            ContextCapture?.FreezeAsync(in outcome) ?? new ValueTask<KevlarContext>(Context);

        public ValueTask DisposeAsync() =>
            ReleaseAttemptResourcesAsync(Context, Cancellation, ContextCapture);
    }

    private static void CopyAttemptProperties(KevlarContext source, KevlarContext target) =>
        source.CopyCompletionPropertiesToParent(target);

    private static bool PropagateAttemptSuppression<T>(
        KevlarContext source,
        KevlarContext target,
        List<HedgeAttempt<T>> pending)
    {
        if (!PropagateAttemptSuppression(source, target))
        {
            return false;
        }

        foreach (var attempt in pending)
        {
            attempt.Context.Properties.SuppressAdditionalAttempts = true;
        }

        return true;
    }

    private static bool PropagateAttemptSuppression(KevlarContext source, KevlarContext target)
    {
        if (!source.Properties.SuppressAdditionalAttempts)
        {
            return false;
        }

        target.Properties.SuppressAdditionalAttempts = true;
        return true;
    }

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

        public int Attempt { get; }

        private OriginalActionContextCapture<T>? ContextCapture { get; }

        public HedgeAttempt<T> AsPending(long startedAt) =>
            new(Execution.AsTask(), Cancellation, Context, Attempt, startedAt, ContextCapture);

        public void Dispose()
        {
            var release = ReleaseAttemptResourcesAsync(Context, Cancellation, ContextCapture);
            if (release.IsCompletedSuccessfully)
            {
                release.GetAwaiter().GetResult();
            }
            else
            {
                _ = release.AsTask();
            }
        }
    }
}

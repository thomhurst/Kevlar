using System.Runtime.ExceptionServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class CircuitBreakerStrategy : Strategy
{
    private readonly Lock _metricsNamesGate = new();
    private readonly HashSet<StrategyMetricAlias> _metricsAliases = [];
    protected internal override bool InvokesContinuationAtMostOnce => true;
    private readonly CircuitBreakerCore _core;
    private readonly OutcomeJudge _judge;

    public CircuitBreakerStrategy(CircuitBreakerOptions options, OutcomeJudge judge)
        : this(
            options,
            judge,
            options.HasHandlingOverride,
            options.BreakDurationGenerator is null
                ? null
                : CircuitBreakerBreakDurationGenerator.Create(options.BreakDurationGenerator),
            options.GetType())
    {
    }

    private CircuitBreakerStrategy(
        CircuitBreakerOptions options,
        OutcomeJudge judge,
        bool hasHandlingOverride,
        CircuitBreakerBreakDurationGenerator? breakDurationGenerator,
        Type optionsType)
    {
        _core = new CircuitBreakerCore(
            options,
            breakDurationGenerator,
            RecordTransitionState,
            optionsType);
        _judge = judge;
        HasHandlingOverride = hasHandlingOverride;
    }

    internal static CircuitBreakerStrategy Create<TResult>(
        CircuitBreakerOptions<TResult> options,
        OutcomeJudge judge) =>
        new(
            options.ToUntyped(),
            judge,
            options.HasHandlingOverride,
            options.BreakDurationGenerator is null
                ? null
                : CircuitBreakerBreakDurationGenerator.Create(options.BreakDurationGenerator),
            options.GetType());

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal CircuitBreakerCore Core => _core;

    public override string Describe() => _core.Describe();

    /// <inheritdoc />
    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var alias = new StrategyMetricAlias(context.ShieldName, context.StrategyIndex);
        var recordState = RegisterMetricsAlias(alias);
        if (_core.RequiresAsyncExecution)
        {
            return ExecuteConfiguredAsync(next, context, alias, recordState);
        }

        if (!_core.TryEnter(
                context.TimeProvider,
                context,
                out var rejection,
                out var admissionGeneration))
        {
            if (recordState)
            {
                RecordState(alias);
            }

            KevlarMetrics.Rejection(context.ShieldName, "circuit_open");
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection!));
        }

        if (recordState)
        {
            try
            {
                RecordState(alias);
            }
            catch
            {
                _core.AbandonProbe(admissionGeneration);
                throw;
            }
        }

        var execution = next.InvokeAsync(context);
        // Stryker disable once all: Route selection is performance-only; both branches call Complete.
        return execution.IsCompletedSuccessfully
            ? new ValueTask<Outcome<T>>(Complete(execution.Result, context, admissionGeneration, alias, recordState))
            : AwaitOutcomeAsync(execution, context, admissionGeneration, alias, recordState);
    }

    private async ValueTask<Outcome<T>> AwaitOutcomeAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        long admissionGeneration,
        StrategyMetricAlias alias,
        bool recordState)
    {
        // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
        var outcome = await execution.ConfigureAwait(false);
        return Complete(outcome, context, admissionGeneration, alias, recordState);
    }

    private Outcome<T> Complete<T>(
        Outcome<T> outcome,
        KevlarContext context,
        long admissionGeneration,
        StrategyMetricAlias alias,
        bool recordState)
    {
        if (_judge.ShouldHandle(in outcome, context, attempt: 0, alias.StrategyIndex))
        {
            _core.RecordFailure(
                context.TimeProvider,
                outcome.Exception,
                context,
                admissionGeneration);
        }
        else if (outcome.Exception is null)
        {
            _core.RecordSuccess(context.TimeProvider, context, admissionGeneration);
        }
        else
        {
            // An unhandled exception says nothing about downstream health; don't move the circuit.
            _core.AbandonProbe(admissionGeneration);
        }

        if (recordState)
        {
            RecordState(alias);
        }

        return outcome;
    }

    private void RecordState(StrategyMetricAlias alias)
    {
        if (KevlarMetrics.CircuitStateEnabled)
        {
            while (true)
            {
                var state = _core.State;
                KevlarMetrics.RecordCircuitState(alias.ShieldName, alias.StrategyIndex, state);
                if (state == _core.State)
                {
                    return;
                }
            }
        }
    }

    private bool RegisterMetricsAlias(StrategyMetricAlias alias)
    {
        if (!KevlarMetrics.CircuitStateEnabled)
        {
            return false;
        }

        lock (_metricsNamesGate)
        {
            if (_metricsAliases.Contains(alias))
            {
                return true;
            }

            if (_metricsAliases.Count >= KevlarMetrics.MaxTrackedStrategyAliases)
            {
                return false;
            }

            _metricsAliases.Add(alias);
            return true;
        }
    }

    private void RecordTransitionState(CircuitState state)
    {
        if (!KevlarMetrics.CircuitStateEnabled)
        {
            return;
        }

        StrategyMetricAlias[] aliases;
        lock (_metricsNamesGate)
        {
            if (_metricsAliases.Count == 0)
            {
                _metricsAliases.Add(new StrategyMetricAlias(null, -1));
            }

            aliases = [.. _metricsAliases];
        }

        List<Exception>? failures = null;
        foreach (var alias in aliases)
        {
            try
            {
                KevlarMetrics.RecordCircuitState(alias.ShieldName, alias.StrategyIndex, state);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is [var failure])
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures).Flatten();
        }
    }

    private ValueTask<Outcome<T>> ExecuteConfiguredAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        StrategyMetricAlias alias,
        bool recordState)
    {
        var entry = _core.TryEnterAsync(context.TimeProvider, context);
        if (!entry.IsCompletedSuccessfully)
        {
            return AwaitConfiguredEntryAsync(entry, next, context, alias, recordState);
        }

        try
        {
            return ExecuteConfiguredEntry(entry.Result, next, context, alias, recordState);
        }
        catch (Exception exception)
        {
            return new ValueTask<Outcome<T>>(Task.FromException<Outcome<T>>(exception));
        }
    }

    private async ValueTask<Outcome<T>> AwaitConfiguredEntryAsync<T, TState>(
        ValueTask<CircuitBreakerCore.EntryResult> entry,
        Continuation<T, TState> next,
        KevlarContext context,
        StrategyMetricAlias alias,
        bool recordState)
    {
        var result = await entry.ConfigureAwait(false);
        return await ExecuteConfiguredEntry(result, next, context, alias, recordState).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> ExecuteConfiguredEntry<T, TState>(
        CircuitBreakerCore.EntryResult entry,
        Continuation<T, TState> next,
        KevlarContext context,
        StrategyMetricAlias alias,
        bool recordState)
    {
        if (!entry.Allowed)
        {
            if (recordState)
            {
                RecordState(alias);
            }

            KevlarMetrics.Rejection(context.ShieldName, "circuit_open");
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(entry.Rejection!));
        }

        if (recordState)
        {
            try
            {
                RecordState(alias);
            }
            catch
            {
                _core.AbandonProbe(entry.AdmissionGeneration);
                throw;
            }
        }

        var execution = next.InvokeAsync(context);
        return execution.IsCompletedSuccessfully
            ? CompleteConfigured(execution.Result, context, entry.AdmissionGeneration, alias, recordState)
            : AwaitConfiguredOutcomeAsync(
                execution,
                context,
                entry.AdmissionGeneration,
                alias,
                recordState);
    }

    private async ValueTask<Outcome<T>> AwaitConfiguredOutcomeAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        long admissionGeneration,
        StrategyMetricAlias alias,
        bool recordState)
    {
        var outcome = await execution.ConfigureAwait(false);
        return await CompleteConfigured(
            outcome,
            context,
            admissionGeneration,
            alias,
            recordState).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> CompleteConfigured<T>(
        Outcome<T> outcome,
        KevlarContext context,
        long admissionGeneration,
        StrategyMetricAlias alias,
        bool recordState)
    {
        ValueTask recording;
        if (_judge.ShouldHandle(in outcome, context, attempt: 0, alias.StrategyIndex))
        {
            recording = _core.RecordFailureAsync(
                context.TimeProvider,
                in outcome,
                context,
                admissionGeneration);
        }
        else if (outcome.Exception is null)
        {
            recording = _core.RecordSuccessAsync(
                context.TimeProvider,
                context,
                admissionGeneration);
        }
        else
        {
            _core.AbandonProbe(admissionGeneration);
            recording = default;
        }

        if (!recording.IsCompletedSuccessfully)
        {
            return AwaitConfiguredRecordingAsync(recording, outcome, alias, recordState);
        }

        recording.GetAwaiter().GetResult();
        if (recordState)
        {
            RecordState(alias);
        }

        return new ValueTask<Outcome<T>>(outcome);
    }

    private async ValueTask<Outcome<T>> AwaitConfiguredRecordingAsync<T>(
        ValueTask recording,
        Outcome<T> outcome,
        StrategyMetricAlias alias,
        bool recordState)
    {
        await recording.ConfigureAwait(false);
        if (recordState)
        {
            RecordState(alias);
        }

        return outcome;
    }
}

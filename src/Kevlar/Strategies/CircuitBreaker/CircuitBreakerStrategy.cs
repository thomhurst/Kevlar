using System.Runtime.CompilerServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class CircuitBreakerStrategy : Strategy
{
    protected internal override bool InvokesContinuationAtMostOnce => true;
    private readonly CircuitBreakerCore _core;
    private readonly OutcomeJudge _judge;
    private readonly KevlarMetrics.StateMetricRegistration<CircuitBreakerStrategy> _metricsRegistration;

    public CircuitBreakerStrategy(CircuitBreakerOptions options, OutcomeJudge judge)
        : this(
            options,
            judge,
            options.HasHandlingOverride,
            CreateBreakDurationGenerator(options),
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
        _metricsRegistration = KevlarMetrics.RegisterCircuitStateSource(this);
        _core = new CircuitBreakerCore(
            options,
            breakDurationGenerator,
            RecordTransitionState,
            optionsType);
        _judge = judge;
        HasHandlingOverride = hasHandlingOverride;
        _core.BindMonitor();
    }

    internal static CircuitBreakerStrategy Create<TResult>(
        CircuitBreakerOptions<TResult> options,
        OutcomeJudge judge) =>
        new(
            options.ToUntyped(),
            judge,
            options.HasHandlingOverride,
            CreateBreakDurationGenerator(options),
            options.GetType());

    private static CircuitBreakerBreakDurationGenerator? CreateBreakDurationGenerator(
        CircuitBreakerOptions options) =>
        options.BreakDurationGenerator is null
            ? null
            : CircuitBreakerBreakDurationGenerator.Create(options.BreakDurationGenerator);

    private static CircuitBreakerBreakDurationGenerator? CreateBreakDurationGenerator<TResult>(
        CircuitBreakerOptions<TResult> options) =>
        options.BreakDurationGenerator is null
            ? null
            : CircuitBreakerBreakDurationGenerator.Create(options.BreakDurationGenerator);

    internal override OutcomeJudge? ReactiveJudge => _judge;

    internal override bool HasHandlingOverride { get; }

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    internal CircuitBreakerCore Core => _core;

    public override string Describe() => _core.Describe();

    /// <inheritdoc />
    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        var alias = new StrategyMetricAlias(context.ShieldName, context.StrategyIndex);
        RegisterMetricsAlias(alias);
        if (_core.RequiresAsyncExecution)
        {
            return ExecuteConfiguredAsync(next, context);
        }

        if (!_core.TryEnter(
                context.TimeProvider,
                context,
                out var rejection,
                out var admissionGeneration))
        {
            KevlarMetrics.Rejection(context, "circuit_open", rejection!, _core.TelemetryName);
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(rejection!));
        }

        var execution = next.InvokeAsync(context);
        // Stryker disable once all: Route selection is performance-only; both branches call Complete.
        return execution.IsCompletedSuccessfully
            ? new ValueTask<Outcome<T>>(Complete(execution.Result, context, admissionGeneration))
            : AwaitOutcomeAsync(execution, context, admissionGeneration);
    }

#if NET8_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    private async ValueTask<Outcome<T>> AwaitOutcomeAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        long admissionGeneration)
    {
        // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
        var outcome = await execution.ConfigureAwait(false);
        return Complete(outcome, context, admissionGeneration);
    }

    private Outcome<T> Complete<T>(
        Outcome<T> outcome,
        KevlarContext context,
        long admissionGeneration)
    {
        if (_judge.ShouldHandle(in outcome, context, attempt: 0, context.StrategyIndex))
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

        return outcome;
    }

    private void RegisterMetricsAlias(StrategyMetricAlias alias)
    {
        if (KevlarMetrics.CircuitStateEnabled)
        {
            _metricsRegistration.Add(alias);
        }
    }

    private void RecordTransitionState(CircuitState _)
    {
        if (KevlarMetrics.CircuitStateEnabled && !_metricsRegistration.HasObservations)
        {
            _metricsRegistration.Add(new StrategyMetricAlias(null, -1));
        }
    }

    private ValueTask<Outcome<T>> ExecuteConfiguredAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var entry = _core.TryEnterAsync(context.TimeProvider, context);
        if (!entry.IsCompletedSuccessfully)
        {
            return AwaitConfiguredEntryAsync(entry, next, context);
        }

        try
        {
            return ExecuteConfiguredEntry(entry.Result, next, context);
        }
        catch (Exception exception)
        {
            return new ValueTask<Outcome<T>>(Task.FromException<Outcome<T>>(exception));
        }
    }

    private async ValueTask<Outcome<T>> AwaitConfiguredEntryAsync<T, TState>(
        ValueTask<CircuitBreakerCore.EntryResult> entry,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        var result = await entry.ConfigureAwait(false);
        return await ExecuteConfiguredEntry(result, next, context).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> ExecuteConfiguredEntry<T, TState>(
        CircuitBreakerCore.EntryResult entry,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        if (!entry.Allowed)
        {
            KevlarMetrics.Rejection(
                context,
                "circuit_open",
                entry.Rejection!,
                _core.TelemetryName);
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(entry.Rejection!));
        }

        var execution = next.InvokeAsync(context);
        return execution.IsCompletedSuccessfully
            ? CompleteConfigured(execution.Result, context, entry.AdmissionGeneration)
            : AwaitConfiguredOutcomeAsync(
                execution,
                context,
                entry.AdmissionGeneration);
    }

    private async ValueTask<Outcome<T>> AwaitConfiguredOutcomeAsync<T>(
        ValueTask<Outcome<T>> execution,
        KevlarContext context,
        long admissionGeneration)
    {
        var outcome = await execution.ConfigureAwait(false);
        return await CompleteConfigured(
            outcome,
            context,
            admissionGeneration).ConfigureAwait(false);
    }

    private ValueTask<Outcome<T>> CompleteConfigured<T>(
        Outcome<T> outcome,
        KevlarContext context,
        long admissionGeneration)
    {
        ValueTask recording;
        if (_judge.ShouldHandle(in outcome, context, attempt: 0, context.StrategyIndex))
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
            return AwaitConfiguredRecordingAsync(recording, outcome);
        }

        recording.GetAwaiter().GetResult();
        return new ValueTask<Outcome<T>>(outcome);
    }

    private async ValueTask<Outcome<T>> AwaitConfiguredRecordingAsync<T>(
        ValueTask recording,
        Outcome<T> outcome)
    {
        await recording.ConfigureAwait(false);
        return outcome;
    }
}

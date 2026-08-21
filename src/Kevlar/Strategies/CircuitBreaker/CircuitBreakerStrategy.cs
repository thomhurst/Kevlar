using System.Runtime.ExceptionServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class CircuitBreakerStrategy : Strategy
{
    private readonly Lock _metricsNamesGate = new();
    private readonly HashSet<string?> _metricsShieldNames = [];
    private readonly CircuitBreakerCore _core;
    private readonly OutcomeJudge _judge;
    private readonly string _metricsInstanceId = KevlarMetrics.CreateStrategyInstanceId();

    public CircuitBreakerStrategy(CircuitBreakerOptions options, OutcomeJudge judge)
    {
        _core = new CircuitBreakerCore(options, RecordTransitionState);
        _judge = judge;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    public override string Describe() => _core.Describe();

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        RegisterMetricsShieldName(context.ShieldName);
        if (!_core.TryEnter(context.TimeProvider, out var rejection))
        {
            RecordState(context.ShieldName);
            KevlarMetrics.Rejection(context.ShieldName, "circuit_open");
            return Outcome<T>.FromException(rejection!);
        }

        RecordState(context.ShieldName);

        var outcome = await next.InvokeAsync(context).ConfigureAwait(false);

        if (_judge.ShouldHandle(in outcome))
        {
            _core.RecordFailure(context.TimeProvider, outcome.Exception);
        }
        else if (outcome.Exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            // A cancelled execution says nothing about downstream health; don't move the circuit.
            _core.AbandonProbe();
        }
        else
        {
            _core.RecordSuccess(context.TimeProvider);
        }

        RecordState(context.ShieldName);

        return outcome;
    }

    private void RecordState(string? shieldName)
    {
        if (KevlarMetrics.CircuitStateEnabled)
        {
            KevlarMetrics.RecordCircuitState(shieldName, _metricsInstanceId, _core.State);
        }
    }

    private void RegisterMetricsShieldName(string? shieldName)
    {
        if (!KevlarMetrics.CircuitStateEnabled)
        {
            return;
        }

        lock (_metricsNamesGate)
        {
            _metricsShieldNames.Add(shieldName);
        }
    }

    private void RecordTransitionState(CircuitState state)
    {
        string?[] shieldNames;
        lock (_metricsNamesGate)
        {
            shieldNames = [.. _metricsShieldNames];
        }

        if (shieldNames.Length == 0)
        {
            KevlarMetrics.RecordCircuitState(null, _metricsInstanceId, state);
            return;
        }

        List<Exception>? failures = null;
        foreach (var shieldName in shieldNames)
        {
            try
            {
                KevlarMetrics.RecordCircuitState(shieldName, _metricsInstanceId, state);
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
}

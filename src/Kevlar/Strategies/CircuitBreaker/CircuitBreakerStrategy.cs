using System.Runtime.ExceptionServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class CircuitBreakerStrategy : Strategy
{
    private readonly Lock _metricsNamesGate = new();
    private readonly HashSet<StrategyMetricAlias> _metricsAliases = [];
    internal override bool InvokesContinuationAtMostOnce => true;
    private readonly CircuitBreakerCore _core;
    private readonly OutcomeJudge _judge;

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
        var alias = new StrategyMetricAlias(context.ShieldName, context.StrategyIndex);
        var recordState = RegisterMetricsAlias(alias);
        if (!_core.TryEnter(context.TimeProvider, out var rejection, out var admittedProbeGeneration))
        {
            if (recordState)
            {
                RecordState(alias);
            }

            KevlarMetrics.Rejection(context.ShieldName, "circuit_open");
            return Outcome<T>.FromException(rejection!);
        }

        if (recordState)
        {
            try
            {
                RecordState(alias);
            }
            catch
            {
                _core.AbandonProbe(admittedProbeGeneration);
                throw;
            }
        }

        var outcome = await next.InvokeAsync(context).ConfigureAwait(false);

        if (_judge.ShouldHandle(in outcome))
        {
            _core.RecordFailure(context.TimeProvider, outcome.Exception);
        }
        else if (outcome.Exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            // A cancelled execution says nothing about downstream health; don't move the circuit.
            _core.AbandonProbe(admittedProbeGeneration);
        }
        else
        {
            _core.RecordSuccess(context.TimeProvider);
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
}

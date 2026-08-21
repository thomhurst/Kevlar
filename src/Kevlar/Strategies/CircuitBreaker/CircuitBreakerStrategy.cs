using System.Runtime.ExceptionServices;
using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class CircuitBreakerStrategy : Strategy
{
    private readonly Lock _metricsNamesGate = new();
    private readonly HashSet<string?> _metricsShieldNames = [];
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
        var recordState = RegisterMetricsShieldName(context.ShieldName);
        if (!_core.TryEnter(context.TimeProvider, out var rejection, out var admittedProbeGeneration))
        {
            if (recordState)
            {
                RecordState(context.ShieldName);
            }

            KevlarMetrics.Rejection(context.ShieldName, "circuit_open");
            return Outcome<T>.FromException(rejection!);
        }

        if (recordState)
        {
            try
            {
                RecordState(context.ShieldName);
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
            RecordState(context.ShieldName);
        }

        return outcome;
    }

    private void RecordState(string? shieldName)
    {
        if (KevlarMetrics.CircuitStateEnabled)
        {
            while (true)
            {
                var state = _core.State;
                KevlarMetrics.RecordCircuitState(shieldName, state);
                if (state == _core.State)
                {
                    return;
                }
            }
        }
    }

    private bool RegisterMetricsShieldName(string? shieldName)
    {
        if (!KevlarMetrics.CircuitStateEnabled)
        {
            return false;
        }

        lock (_metricsNamesGate)
        {
            if (_metricsShieldNames.Contains(shieldName))
            {
                return true;
            }

            if (_metricsShieldNames.Count >= KevlarMetrics.MaxTrackedStrategyAliases)
            {
                return false;
            }

            _metricsShieldNames.Add(shieldName);
            return true;
        }
    }

    private void RecordTransitionState(CircuitState state)
    {
        string?[] shieldNames;
        lock (_metricsNamesGate)
        {
            if (_metricsShieldNames.Count == 0)
            {
                _metricsShieldNames.Add(null);
            }

            shieldNames = [.. _metricsShieldNames];
        }

        List<Exception>? failures = null;
        foreach (var shieldName in shieldNames)
        {
            try
            {
                KevlarMetrics.RecordCircuitState(shieldName, state);
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

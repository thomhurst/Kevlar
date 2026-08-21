using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class CircuitBreakerStrategy : Strategy
{
    private readonly CircuitBreakerCore _core;
    private readonly OutcomeJudge _judge;

    public CircuitBreakerStrategy(CircuitBreakerOptions options, OutcomeJudge judge)
    {
        _core = new CircuitBreakerCore(options);
        _judge = judge;
    }

    internal override OutcomeJudge? ReactiveJudge => _judge;

    protected internal override bool IsDuplicateReferenceUnsafe => true;

    public override string Describe() => _core.Describe();

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
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
            KevlarMetrics.RecordCircuitState(shieldName, _core.State);
        }
    }
}

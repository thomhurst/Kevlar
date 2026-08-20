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

    public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
    {
        if (!_core.TryEnter(context.TimeProvider, out var rejection))
        {
            return Outcome<T>.FromException(rejection!);
        }

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

        return outcome;
    }
}

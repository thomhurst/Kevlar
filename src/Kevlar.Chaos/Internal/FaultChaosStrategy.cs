namespace Kevlar.Chaos.Internal;

internal sealed class FaultChaosStrategy : ChaosStrategy
{
    private readonly Exception _exception;
    private readonly Func<KevlarContext, Exception>? _exceptionGenerator;

    public FaultChaosStrategy(ChaosFaultOptions options)
        : base(options)
    {
        _exception = options.Exception ?? new ChaosInjectedException();
        _exceptionGenerator = options.ExceptionGenerator;
    }

    public override string Describe() => _exceptionGenerator is null
        ? $"ChaosFault({_exception.GetType().Name})"
        : "ChaosFault(dynamic)";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        if (!TryDecide(context, out var decision))
        {
            return next.InvokeAsync(context);
        }

        var exception = _exceptionGenerator is null ? _exception : _exceptionGenerator(context);
        if (exception is null)
        {
            throw new InvalidOperationException("The chaos exception generator returned null.");
        }

        Notify(ChaosInjectionKind.Fault, context, decision);
        return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
    }
}

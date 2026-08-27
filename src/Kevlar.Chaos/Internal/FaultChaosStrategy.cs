namespace Kevlar.Chaos.Internal;

internal sealed class FaultChaosStrategy : ChaosStrategy
{
    private readonly Exception _exception;
    private readonly Func<KevlarContext, ValueTask<Exception>>? _exceptionGenerator;

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
        var decision = DecideAsync(context);
        return decision.IsCompletedSuccessfully
            ? ExecuteFromDecision(next, context, decision.GetAwaiter().GetResult())
            : ExecuteAfterDecisionAsync(next, context, decision);
    }

    private ValueTask<Outcome<T>> ExecuteFromDecision<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        ChaosDecision? decision)
    {
        if (decision is not { } injection)
        {
            return next.InvokeAsync(context);
        }

        if (_exceptionGenerator is null)
        {
            return Inject<T>(_exception, context, injection);
        }

        var exception = InvokeGenerator(
            _exceptionGenerator,
            context,
            context,
            "ChaosFaultOptions.ExceptionGenerator");
        return exception.IsCompletedSuccessfully
            ? Inject<T>(exception.GetAwaiter().GetResult(), context, injection)
            : InjectAfterGenerationAsync<T>(exception, context, injection);
    }

    private ValueTask<Outcome<T>> Inject<T>(
        Exception exception,
        KevlarContext context,
        ChaosDecision decision)
    {
        if (exception is null)
        {
            throw new InvalidOperationException("The chaos exception generator returned null.");
        }

        var notification = Notify(ChaosInjectionKind.Fault, context, decision, exception);
        return notification.IsCompletedSuccessfully
            ? new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception))
            : InjectAfterNotificationAsync<T>(notification, exception);
    }

    private async ValueTask<Outcome<T>> ExecuteAfterDecisionAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        ValueTask<ChaosDecision?> decision) =>
        await ExecuteFromDecision(next, context, await decision.ConfigureAwait(false)).ConfigureAwait(false);

    private async ValueTask<Outcome<T>> InjectAfterGenerationAsync<T>(
        ValueTask<Exception> exception,
        KevlarContext context,
        ChaosDecision decision) =>
        await Inject<T>(await exception.ConfigureAwait(false), context, decision).ConfigureAwait(false);

    private static async ValueTask<Outcome<T>> InjectAfterNotificationAsync<T>(
        ValueTask notification,
        Exception exception)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromException(exception);
    }
}

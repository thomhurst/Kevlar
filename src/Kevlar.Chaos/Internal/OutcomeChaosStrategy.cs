using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Kevlar.Chaos.Internal;

internal sealed class OutcomeChaosStrategy<TResult> : ChaosStrategy
{
    private readonly TResult? _result;
    private readonly Func<KevlarContext, ValueTask<TResult>>? _resultGenerator;

    public OutcomeChaosStrategy(ChaosOutcomeOptions<TResult> options)
        : base(options)
    {
        _result = options.Result;
        _resultGenerator = options.ResultGenerator;
    }

    public override string Describe() => _resultGenerator is null
        ? "ChaosOutcome"
        : "ChaosOutcome(dynamic)";

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

        Debug.Assert(typeof(T) == typeof(TResult), "Outcome chaos only executes inside a matching Shield<TResult>.");
        if (_resultGenerator is null)
        {
            return Inject<T>(_result, context, injection);
        }

        var result = InvokeGenerator(
            _resultGenerator,
            context,
            context,
            "ChaosOutcomeOptions.ResultGenerator");
        return result.IsCompletedSuccessfully
            ? Inject<T>(result.GetAwaiter().GetResult(), context, injection)
            : InjectAfterGenerationAsync<T>(result, context, injection);
    }

    private ValueTask<Outcome<T>> Inject<T>(
        TResult? typedResult,
        KevlarContext context,
        ChaosDecision decision)
    {
        var result = Unsafe.As<TResult?, T>(ref typedResult);

        var notification = Notify(ChaosInjectionKind.Outcome, context, decision);
        return notification.IsCompletedSuccessfully
            ? new ValueTask<Outcome<T>>(Outcome<T>.FromResult(result))
            : InjectAfterNotificationAsync(notification, result);
    }

    private async ValueTask<Outcome<T>> ExecuteAfterDecisionAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        ValueTask<ChaosDecision?> decision) =>
        await ExecuteFromDecision(next, context, await decision.ConfigureAwait(false)).ConfigureAwait(false);

    private async ValueTask<Outcome<T>> InjectAfterGenerationAsync<T>(
        ValueTask<TResult> result,
        KevlarContext context,
        ChaosDecision decision) =>
        await Inject<T>(await result.ConfigureAwait(false), context, decision).ConfigureAwait(false);

    private static async ValueTask<Outcome<T>> InjectAfterNotificationAsync<T>(
        ValueTask notification,
        T result)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromResult(result);
    }
}

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Kevlar.Chaos.Internal;

internal sealed class OutcomeChaosStrategy<TResult> : ChaosStrategy
{
    private readonly TResult? _result;
    private readonly Func<KevlarContext, TResult>? _resultGenerator;

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
        if (!TryDecide(context, out var decision))
        {
            return next.InvokeAsync(context);
        }

        Debug.Assert(typeof(T) == typeof(TResult), "Outcome chaos only executes inside a matching Shield<TResult>.");
        var typedResult = _resultGenerator is null ? _result : _resultGenerator(context);
        var result = Unsafe.As<TResult?, T>(ref typedResult);

        var notification = Notify(ChaosInjectionKind.Outcome, context, decision);
        return notification.IsCompletedSuccessfully
            ? new ValueTask<Outcome<T>>(Outcome<T>.FromResult(result))
            : InjectAfterNotificationAsync(notification, result);
    }

    private static async ValueTask<Outcome<T>> InjectAfterNotificationAsync<T>(
        ValueTask notification,
        T result)
    {
        await notification.ConfigureAwait(false);
        return Outcome<T>.FromResult(result);
    }
}

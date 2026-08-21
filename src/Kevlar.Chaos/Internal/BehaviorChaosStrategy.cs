namespace Kevlar.Chaos.Internal;

internal sealed class BehaviorChaosStrategy : ChaosStrategy
{
    private readonly Func<KevlarContext, ValueTask>? _behavior;

    public BehaviorChaosStrategy(ChaosBehaviorOptions options)
        : base(options)
    {
        _behavior = options.Behavior;
    }

    public override string Describe() => "ChaosBehavior";

    public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context)
    {
        if (_behavior is null)
        {
            return next.InvokeAsync(context);
        }

        if (!TryDecide(context, out var decision))
        {
            return next.InvokeAsync(context);
        }

        Notify(ChaosInjectionKind.Behavior, context, decision);
        var behavior = _behavior(context);
        if (behavior.IsCompletedSuccessfully)
        {
            behavior.GetAwaiter().GetResult();
            return next.InvokeAsync(context);
        }

        return AwaitBehaviorAsync(behavior, next, context);
    }

    private static async ValueTask<Outcome<T>> AwaitBehaviorAsync<T, TState>(
        ValueTask behavior,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        await behavior.ConfigureAwait(false);
        return await next.InvokeAsync(context).ConfigureAwait(false);
    }
}

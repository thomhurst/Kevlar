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
        var behavior = _behavior;
        if (behavior is null)
        {
            return next.InvokeAsync(context);
        }

        var decision = DecideAsync(context);
        return decision.IsCompletedSuccessfully
            ? ExecuteFromDecision(behavior, next, context, decision.GetAwaiter().GetResult())
            : ExecuteAfterDecisionAsync(behavior, next, context, decision);
    }

    private ValueTask<Outcome<T>> ExecuteFromDecision<T, TState>(
        Func<KevlarContext, ValueTask> behavior,
        Continuation<T, TState> next,
        KevlarContext context,
        ChaosDecision? decision)
    {
        if (decision is not { } injection)
        {
            return next.InvokeAsync(context);
        }

        var notification = Notify(ChaosInjectionKind.Behavior, context, injection);
        if (!notification.IsCompletedSuccessfully)
        {
            return AwaitNotificationThenBehaviorAsync(notification, behavior, next, context);
        }

        var execution = behavior(context);
        if (execution.IsCompletedSuccessfully)
        {
            execution.GetAwaiter().GetResult();
            return next.InvokeAsync(context);
        }

        ThrowIfSynchronousExecutionCannotAwait(execution, context, "ChaosBehaviorOptions.Behavior");
        return AwaitBehaviorAsync(execution, next, context);
    }

    private async ValueTask<Outcome<T>> ExecuteAfterDecisionAsync<T, TState>(
        Func<KevlarContext, ValueTask> behavior,
        Continuation<T, TState> next,
        KevlarContext context,
        ValueTask<ChaosDecision?> decision) =>
        await ExecuteFromDecision(
            behavior,
            next,
            context,
            await decision.ConfigureAwait(false)).ConfigureAwait(false);

    private static async ValueTask<Outcome<T>> AwaitNotificationThenBehaviorAsync<T, TState>(
        ValueTask notification,
        Func<KevlarContext, ValueTask> behavior,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        await notification.ConfigureAwait(false);
        await behavior(context).ConfigureAwait(false);
        return await next.InvokeAsync(context).ConfigureAwait(false);
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

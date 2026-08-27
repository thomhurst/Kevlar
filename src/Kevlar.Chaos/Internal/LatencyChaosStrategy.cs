namespace Kevlar.Chaos.Internal;

internal sealed class LatencyChaosStrategy : ChaosStrategy
{
    private readonly TimeSpan _delay;
    private readonly Func<KevlarContext, ValueTask<TimeSpan>>? _delayGenerator;

    public LatencyChaosStrategy(ChaosLatencyOptions options)
        : base(options)
    {
        ValidateDelay(options.Delay, nameof(options.Delay));
        _delay = options.Delay;
        _delayGenerator = options.DelayGenerator;
    }

    public override string Describe() => _delayGenerator is null
        ? $"ChaosLatency({_delay.TotalMilliseconds:0.###}ms)"
        : "ChaosLatency(dynamic)";

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

        if (_delayGenerator is null)
        {
            return Inject(_delay, next, context, injection);
        }

        var delay = InvokeGenerator(
            _delayGenerator,
            context,
            context,
            "ChaosLatencyOptions.DelayGenerator");
        return delay.IsCompletedSuccessfully
            ? Inject(delay.GetAwaiter().GetResult(), next, context, injection)
            : InjectAfterGenerationAsync(delay, next, context, injection);
    }

    private ValueTask<Outcome<T>> Inject<T, TState>(
        TimeSpan delay,
        Continuation<T, TState> next,
        KevlarContext context,
        ChaosDecision decision)
    {
        ValidateDelay(delay, "generated delay");
        var notification = Notify(ChaosInjectionKind.Latency, context, decision);
        if (!notification.IsCompletedSuccessfully)
        {
            return AwaitNotificationThenDelayAsync(notification, delay, next, context);
        }

        var wait = ChaosDelay.DelayAsync(context, delay);
        if (wait.IsCompletedSuccessfully)
        {
            wait.GetAwaiter().GetResult();
            return next.InvokeAsync(context);
        }

        return AwaitDelayAsync(wait, next, context);
    }

    private async ValueTask<Outcome<T>> ExecuteAfterDecisionAsync<T, TState>(
        Continuation<T, TState> next,
        KevlarContext context,
        ValueTask<ChaosDecision?> decision) =>
        await ExecuteFromDecision(next, context, await decision.ConfigureAwait(false)).ConfigureAwait(false);

    private async ValueTask<Outcome<T>> InjectAfterGenerationAsync<T, TState>(
        ValueTask<TimeSpan> delay,
        Continuation<T, TState> next,
        KevlarContext context,
        ChaosDecision decision) =>
        await Inject(await delay.ConfigureAwait(false), next, context, decision).ConfigureAwait(false);

    private static async ValueTask<Outcome<T>> AwaitNotificationThenDelayAsync<T, TState>(
        ValueTask notification,
        TimeSpan delay,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        await notification.ConfigureAwait(false);
        await ChaosDelay.DelayAsync(context, delay).ConfigureAwait(false);
        return await next.InvokeAsync(context).ConfigureAwait(false);
    }

    private static async ValueTask<Outcome<T>> AwaitDelayAsync<T, TState>(
        ValueTask wait,
        Continuation<T, TState> next,
        KevlarContext context)
    {
        await wait.ConfigureAwait(false);
        return await next.InvokeAsync(context).ConfigureAwait(false);
    }

    private static void ValidateDelay(TimeSpan delay, string parameterName)
    {
        if (delay < TimeSpan.Zero || delay > ChaosDelay.Maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                delay,
                "Chaos latency must be non-negative and within the runtime timer limit.");
        }
    }
}

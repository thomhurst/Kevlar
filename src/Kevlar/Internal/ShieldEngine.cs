namespace Kevlar.Internal;

/// <summary>Boundary plumbing shared by <see cref="Shield"/> and <see cref="Shield{TResult}"/>.</summary>
internal static class ShieldEngine
{
    public static ValueTask<T> ExecuteAsync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Rethrow<T>(Outcome<T>.FromException(new OperationCanceledException(cancellationToken)));
        }

        if (head is null)
        {
            // No strategies: skip the context and outcome machinery entirely.
            return action(state, cancellationToken);
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: false, timeProvider, shieldName);
        var pipeline = RunAsync(head, state, action, context);

        if (pipeline.IsCompletedSuccessfully)
        {
            var outcome = pipeline.Result;
            KevlarContext.Return(context);
            KevlarMetrics.Execution(shieldName, outcome.IsSuccess);
            return outcome.IsSuccess ? new ValueTask<T>(outcome.Result!) : Rethrow(outcome);
        }

        return AwaitAsync(pipeline, context);
    }

    public static ValueTask<Outcome<T>> ExecuteOutcomeAsync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(new OperationCanceledException(cancellationToken)));
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: false, timeProvider, shieldName);
        var pipeline = RunAsync(head, state, action, context);

        if (pipeline.IsCompletedSuccessfully)
        {
            var outcome = pipeline.Result;
            KevlarContext.Return(context);
            KevlarMetrics.Execution(shieldName, outcome.IsSuccess);
            return new ValueTask<Outcome<T>>(outcome);
        }

        return AwaitOutcomeAsync(pipeline, context);
    }

    public static T ExecuteSync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, T> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (head is null)
        {
            return action(state, cancellationToken);
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: true, timeProvider, shieldName);

        try
        {
            var pipeline = RunSync(head, state, action, context);
            var outcome = pipeline.IsCompletedSuccessfully
                ? pipeline.Result
                : pipeline.AsTask().GetAwaiter().GetResult();

            KevlarMetrics.Execution(shieldName, outcome.IsSuccess);
            return outcome.GetResultOrRethrow();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    private static ValueTask<Outcome<T>> RunAsync<T, TState>(
        StrategyNode? head,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> action,
        KevlarContext context)
    {
        var continuation = new Continuation<T, AsyncCallback<TState, T>>(
            head,
            static (callback, ctx) => InvokeAsync(callback, ctx),
            new AsyncCallback<TState, T>(state, action));

        return continuation.InvokeAsync(context);
    }

    private static ValueTask<Outcome<T>> RunSync<T, TState>(
        StrategyNode? head,
        TState state,
        Func<TState, CancellationToken, T> action,
        KevlarContext context)
    {
        var continuation = new Continuation<T, SyncCallback<TState, T>>(
            head,
            static (callback, ctx) =>
            {
                try
                {
                    return new ValueTask<Outcome<T>>(Outcome<T>.FromResult(callback.Action(callback.State, ctx.CancellationToken)));
                }
                catch (Exception exception)
                {
                    return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
                }
            },
            new SyncCallback<TState, T>(state, action));

        return continuation.InvokeAsync(context);
    }

    private static async ValueTask<Outcome<T>> InvokeAsync<TState, T>(AsyncCallback<TState, T> callback, KevlarContext context)
    {
        try
        {
            return Outcome<T>.FromResult(await callback.Action(callback.State, context.CancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }
    }

    private static async ValueTask<T> AwaitAsync<T>(ValueTask<Outcome<T>> pipeline, KevlarContext context)
    {
        try
        {
            var outcome = await pipeline.ConfigureAwait(false);
            KevlarMetrics.Execution(context.ShieldName, outcome.IsSuccess);
            return outcome.GetResultOrRethrow();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    private static async ValueTask<Outcome<T>> AwaitOutcomeAsync<T>(ValueTask<Outcome<T>> pipeline, KevlarContext context)
    {
        try
        {
            var outcome = await pipeline.ConfigureAwait(false);
            KevlarMetrics.Execution(context.ShieldName, outcome.IsSuccess);
            return outcome;
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    private static async ValueTask<T> Rethrow<T>(Outcome<T> outcome)
    {
        outcome.GetResultOrRethrow();
        return default!;
    }

    private readonly struct AsyncCallback<TState, T>
    {
        public AsyncCallback(TState state, Func<TState, CancellationToken, ValueTask<T>> action)
        {
            State = state;
            Action = action;
        }

        public TState State { get; }

        public Func<TState, CancellationToken, ValueTask<T>> Action { get; }
    }

    private readonly struct SyncCallback<TState, T>
    {
        public SyncCallback(TState state, Func<TState, CancellationToken, T> action)
        {
            State = state;
            Action = action;
        }

        public TState State { get; }

        public Func<TState, CancellationToken, T> Action { get; }
    }
}

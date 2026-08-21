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
        var startedAt = KevlarMetrics.DurationEnabled ? KevlarMetrics.StartDuration() : 0;
        if (cancellationToken.IsCancellationRequested)
        {
            RecordExecution(startedAt, shieldName, success: false);
            return Rethrow<T>(Outcome<T>.FromException(new OperationCanceledException(cancellationToken)));
        }

        if (head is null)
        {
            if (!KevlarMetrics.ExecutionEnabled && !KevlarMetrics.DurationEnabled)
            {
                return action(state, cancellationToken);
            }

            try
            {
                var execution = action(state, cancellationToken);
                if (execution.IsCompletedSuccessfully)
                {
                    RecordExecution(startedAt, shieldName, success: true);
                    return execution;
                }

                return AwaitDirectAsync(execution, shieldName, startedAt);
            }
            catch
            {
                RecordExecution(startedAt, shieldName, success: false);
                throw;
            }
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: false, timeProvider, shieldName);
        var pipeline = RunAsync(head, state, action, context);

        if (pipeline.IsCompletedSuccessfully)
        {
            var outcome = pipeline.Result;
            KevlarContext.Return(context);
            RecordExecution(startedAt, shieldName, outcome.IsSuccess);
            return outcome.IsSuccess ? new ValueTask<T>(outcome.Result!) : Rethrow(outcome);
        }

        return AwaitAsync(pipeline, context, startedAt);
    }

    public static ValueTask<Outcome<T>> ExecuteOutcomeAsync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        var startedAt = KevlarMetrics.DurationEnabled ? KevlarMetrics.StartDuration() : 0;
        if (cancellationToken.IsCancellationRequested)
        {
            RecordExecution(startedAt, shieldName, success: false);
            return new ValueTask<Outcome<T>>(Outcome<T>.FromException(new OperationCanceledException(cancellationToken)));
        }

        if (head is null)
        {
            ValueTask<T> execution;
            try
            {
                execution = action(state, cancellationToken);
            }
            catch (Exception exception)
            {
                RecordExecution(startedAt, shieldName, success: false);
                return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
            }

            if (execution.IsCompletedSuccessfully)
            {
                RecordExecution(startedAt, shieldName, success: true);
                return new ValueTask<Outcome<T>>(Outcome<T>.FromResult(execution.Result));
            }

            return AwaitDirectOutcomeAsync(execution, shieldName, startedAt);
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: false, timeProvider, shieldName);
        var pipeline = RunAsync(head, state, action, context);

        if (pipeline.IsCompletedSuccessfully)
        {
            var outcome = pipeline.Result;
            KevlarContext.Return(context);
            RecordExecution(startedAt, shieldName, outcome.IsSuccess);
            return new ValueTask<Outcome<T>>(outcome);
        }

        return AwaitOutcomeAsync(pipeline, context, startedAt);
    }

    public static ValueTask<T> ExecuteWithContextAsync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        var startedAt = KevlarMetrics.DurationEnabled ? KevlarMetrics.StartDuration() : 0;
        if (cancellationToken.IsCancellationRequested)
        {
            RecordExecution(startedAt, shieldName, success: false);
            return Rethrow<T>(Outcome<T>.FromException(new OperationCanceledException(cancellationToken)));
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: false, timeProvider, shieldName);
        try
        {
            initializeProperties(state, context.Properties);
        }
        catch
        {
            KevlarContext.Return(context);
            RecordExecution(startedAt, shieldName, success: false);
            throw;
        }

        var pipeline = RunWithContextAsync(head, state, action, context);
        if (pipeline.IsCompletedSuccessfully)
        {
            var outcome = pipeline.Result;
            KevlarContext.Return(context);
            RecordExecution(startedAt, shieldName, outcome.IsSuccess);
            return outcome.IsSuccess ? new ValueTask<T>(outcome.Result!) : Rethrow(outcome);
        }

        return AwaitAsync(pipeline, context, startedAt);
    }

    public static T ExecuteSync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, T> action,
        CancellationToken cancellationToken)
    {
        var startedAt = KevlarMetrics.DurationEnabled ? KevlarMetrics.StartDuration() : 0;
        if (cancellationToken.IsCancellationRequested)
        {
            RecordExecution(startedAt, shieldName, success: false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (head is null)
        {
            try
            {
                var result = action(state, cancellationToken);
                RecordExecution(startedAt, shieldName, success: true);
                return result;
            }
            catch
            {
                RecordExecution(startedAt, shieldName, success: false);
                throw;
            }
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: true, timeProvider, shieldName);

        try
        {
            var pipeline = RunSync(head, state, action, context);
            var outcome = pipeline.IsCompletedSuccessfully
                ? pipeline.Result
                : pipeline.AsTask().GetAwaiter().GetResult();

            RecordExecution(startedAt, shieldName, outcome.IsSuccess);
            return outcome.GetResultOrRethrowInternal();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    public static T ExecuteWithContextSync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, T> action,
        CancellationToken cancellationToken)
    {
        var startedAt = KevlarMetrics.DurationEnabled ? KevlarMetrics.StartDuration() : 0;
        if (cancellationToken.IsCancellationRequested)
        {
            RecordExecution(startedAt, shieldName, success: false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: true, timeProvider, shieldName);
        try
        {
            try
            {
                initializeProperties(state, context.Properties);
            }
            catch
            {
                RecordExecution(startedAt, shieldName, success: false);
                throw;
            }

            var pipeline = RunWithContextSync(head, state, action, context);
            var outcome = pipeline.IsCompletedSuccessfully
                ? pipeline.Result
                : pipeline.AsTask().GetAwaiter().GetResult();

            RecordExecution(startedAt, shieldName, outcome.IsSuccess);
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

    private static ValueTask<Outcome<T>> RunWithContextAsync<T, TState>(
        StrategyNode? head,
        TState state,
        Func<TState, KevlarContext, ValueTask<T>> action,
        KevlarContext context)
    {
        var continuation = new Continuation<T, ContextAsyncCallback<TState, T>>(
            head,
            static (callback, ctx) => InvokeWithContextAsync(callback, ctx),
            new ContextAsyncCallback<TState, T>(state, action));

        return continuation.InvokeAsync(context);
    }

    private static ValueTask<Outcome<T>> RunWithContextSync<T, TState>(
        StrategyNode? head,
        TState state,
        Func<TState, KevlarContext, T> action,
        KevlarContext context)
    {
        var continuation = new Continuation<T, ContextSyncCallback<TState, T>>(
            head,
            static (callback, ctx) =>
            {
                try
                {
                    return new ValueTask<Outcome<T>>(Outcome<T>.FromResult(callback.Action(callback.State, ctx)));
                }
                catch (Exception exception)
                {
                    return new ValueTask<Outcome<T>>(Outcome<T>.FromException(exception));
                }
            },
            new ContextSyncCallback<TState, T>(state, action));

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

    private static async ValueTask<Outcome<T>> InvokeWithContextAsync<TState, T>(
        ContextAsyncCallback<TState, T> callback,
        KevlarContext context)
    {
        try
        {
            return Outcome<T>.FromResult(await callback.Action(callback.State, context).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }
    }

    private static async ValueTask<T> AwaitAsync<T>(ValueTask<Outcome<T>> pipeline, KevlarContext context, long startedAt)
    {
        try
        {
            var outcome = await pipeline.ConfigureAwait(false);
            RecordExecution(startedAt, context.ShieldName, outcome.IsSuccess);
            return outcome.GetResultOrRethrowInternal();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    private static async ValueTask<T> AwaitDirectAsync<T>(ValueTask<T> execution, string? shieldName, long startedAt)
    {
        try
        {
            var result = await execution.ConfigureAwait(false);
            RecordExecution(startedAt, shieldName, success: true);
            return result;
        }
        catch
        {
            RecordExecution(startedAt, shieldName, success: false);
            throw;
        }
    }

    private static async ValueTask<Outcome<T>> AwaitDirectOutcomeAsync<T>(
        ValueTask<T> execution,
        string? shieldName,
        long startedAt)
    {
        Outcome<T> outcome;
        try
        {
            outcome = Outcome<T>.FromResult(await execution.ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            outcome = Outcome<T>.FromException(exception);
        }

        RecordExecution(startedAt, shieldName, outcome.IsSuccess);
        return outcome;
    }

    private static async ValueTask<Outcome<T>> AwaitOutcomeAsync<T>(
        ValueTask<Outcome<T>> pipeline,
        KevlarContext context,
        long startedAt)
    {
        try
        {
            var outcome = await pipeline.ConfigureAwait(false);
            RecordExecution(startedAt, context.ShieldName, outcome.IsSuccess);
            return outcome;
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    private static void RecordExecution(long startedAt, string? shieldName, bool success)
    {
        KevlarMetrics.Duration(startedAt, shieldName, success);
        KevlarMetrics.Execution(shieldName, success);
    }

    private static async ValueTask<T> Rethrow<T>(Outcome<T> outcome)
    {
        outcome.GetResultOrRethrowInternal();
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

    private readonly struct ContextAsyncCallback<TState, T>
    {
        public ContextAsyncCallback(TState state, Func<TState, KevlarContext, ValueTask<T>> action)
        {
            State = state;
            Action = action;
        }

        public TState State { get; }

        public Func<TState, KevlarContext, ValueTask<T>> Action { get; }
    }

    private readonly struct ContextSyncCallback<TState, T>
    {
        public ContextSyncCallback(TState state, Func<TState, KevlarContext, T> action)
        {
            State = state;
            Action = action;
        }

        public TState State { get; }

        public Func<TState, KevlarContext, T> Action { get; }
    }
}

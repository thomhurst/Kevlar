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
            try
            {
                if (!KevlarMetrics.ExecutionEnabled && !KevlarMetrics.DurationEnabled)
                {
                    return action(state, cancellationToken);
                }

                var execution = action(state, cancellationToken);
                if (execution.IsCompletedSuccessfully)
                {
                    RecordExecution(startedAt, shieldName, success: true);
                    return execution;
                }

                return AwaitDirectAsync(execution, shieldName, startedAt);
            }
            catch (Exception exception)
            {
                RecordExecution(startedAt, shieldName, success: false);
                return Rethrow<T>(Outcome<T>.FromException(exception));
            }
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: false, timeProvider, shieldName);
        var pipeline = RunAsync(head, state, action, context);

        if (pipeline.IsCompletedSuccessfully)
        {
            var outcome = pipeline.Result;
            RecordExecution(startedAt, context, outcome.IsSuccess);
            KevlarContext.Return(context);
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
            RecordExecution(startedAt, context, outcome.IsSuccess);
            KevlarContext.Return(context);
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
        CancellationToken cancellationToken) =>
        ExecuteWithContextAsyncCore(
            head,
            timeProvider,
            shieldName,
            state,
            initializeProperties,
            action,
            onCompleted: null,
            cancellationToken);

    public static ValueTask<T> ExecuteWithContextAsync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask<T>> action,
        Action<TState, KevlarProperties> onCompleted,
        CancellationToken cancellationToken)
    {
        Throw.IfNull(onCompleted, nameof(onCompleted));
        return ExecuteWithContextAsyncCore(
            head,
            timeProvider,
            shieldName,
            state,
            initializeProperties,
            action,
            onCompleted: (Action<TState, KevlarProperties>?)onCompleted,
            cancellationToken);
    }

    private static ValueTask<T> ExecuteWithContextAsyncCore<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask<T>> action,
        Action<TState, KevlarProperties>? onCompleted,
        CancellationToken cancellationToken)
    {
        var startedAt = KevlarMetrics.DurationEnabled ? KevlarMetrics.StartDuration() : 0;
        if (cancellationToken.IsCancellationRequested)
        {
            RecordExecution(startedAt, shieldName, success: false);
            if (onCompleted is not null)
            {
                var cancelledContext = KevlarContext.Rent(
                    cancellationToken,
                    isSynchronous: false,
                    timeProvider,
                    shieldName);
                NotifyCompleted(onCompleted, state, cancelledContext.PropertiesForCompletion);
                KevlarContext.Return(cancelledContext);
            }

            return Rethrow<T>(Outcome<T>.FromException(new OperationCanceledException(cancellationToken)));
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: false, timeProvider, shieldName);
        try
        {
            initializeProperties(state, context.Properties);
        }
        catch
        {
            RecordExecution(startedAt, context, success: false);
            NotifyCompleted(onCompleted, state, context.PropertiesForCompletion);
            KevlarContext.Return(context);
            throw;
        }

        var pipeline = RunWithContextAsync(head, state, action, context);
        if (pipeline.IsCompletedSuccessfully)
        {
            var outcome = pipeline.Result;
            RecordExecution(startedAt, context, outcome.IsSuccess);
            NotifyCompleted(onCompleted, state, context.PropertiesForCompletion);
            KevlarContext.Return(context);
            return outcome.IsSuccess ? new ValueTask<T>(outcome.Result!) : Rethrow(outcome);
        }

        return AwaitWithContextAsync(pipeline, context, state, onCompleted, startedAt);
    }

    public static ValueTask<T> ExecuteWithParentContextAsync<T, TState>(
        StrategyNode? head,
        string? shieldName,
        TState state,
        Func<TState, KevlarContext, ValueTask<T>> action,
        KevlarContext parentContext)
    {
        var startedAt = KevlarMetrics.DurationEnabled ? KevlarMetrics.StartDuration() : 0;
        var cancellationToken = parentContext.CancellationToken;
        if (cancellationToken.IsCancellationRequested)
        {
            RecordExecution(startedAt, shieldName, success: false);
            return Rethrow<T>(Outcome<T>.FromException(new OperationCanceledException(cancellationToken)));
        }

        var context = KevlarContext.RentChild(parentContext, shieldName, isSynchronous: false);
        var pipeline = RunWithContextAsync(head, state, action, context);
        if (pipeline.IsCompletedSuccessfully)
        {
            var outcome = pipeline.Result;
            RecordExecution(startedAt, shieldName, outcome.IsSuccess);
            ReturnChildContext(context, parentContext);
            return outcome.IsSuccess ? new ValueTask<T>(outcome.Result!) : Rethrow(outcome);
        }

        return AwaitWithParentContextAsync(pipeline, context, parentContext, startedAt);
    }

    public static T ExecuteWithParentContextSync<T, TState>(
        StrategyNode? head,
        string? shieldName,
        TState state,
        Func<TState, KevlarContext, T> action,
        KevlarContext parentContext)
    {
        var startedAt = KevlarMetrics.DurationEnabled ? KevlarMetrics.StartDuration() : 0;
        var cancellationToken = parentContext.CancellationToken;
        if (cancellationToken.IsCancellationRequested)
        {
            RecordExecution(startedAt, shieldName, success: false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        ValidateSynchronousExecution(head, startedAt, shieldName);

        var context = KevlarContext.RentChild(parentContext, shieldName, isSynchronous: true);
        try
        {
            var pipeline = RunWithContextSync(head, state, action, context);
            var outcome = pipeline.IsCompletedSuccessfully
                ? pipeline.Result
                : pipeline.AsTask().GetAwaiter().GetResult();

            RecordExecution(startedAt, context, outcome.IsSuccess);
            return outcome.GetResultOrRethrow();
        }
        finally
        {
            ReturnChildContext(context, parentContext);
        }
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

        ValidateSynchronousExecution(head, startedAt, shieldName);

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: true, timeProvider, shieldName);

        try
        {
            var pipeline = RunSync(head, state, action, context);
            var outcome = pipeline.IsCompletedSuccessfully
                ? pipeline.Result
                : pipeline.AsTask().GetAwaiter().GetResult();

            RecordExecution(startedAt, context, outcome.IsSuccess);
            return outcome.GetResultOrRethrowInternal();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    public static Outcome<T> ExecuteOutcomeSync<T, TState>(
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
            return Outcome<T>.FromException(new OperationCanceledException(cancellationToken));
        }

        if (head is null)
        {
            Outcome<T> directOutcome;
            try
            {
                directOutcome = Outcome<T>.FromResult(action(state, cancellationToken));
            }
            catch (Exception exception)
            {
                directOutcome = Outcome<T>.FromException(exception);
            }

            RecordExecution(startedAt, shieldName, directOutcome.IsSuccess);
            return directOutcome;
        }

        try
        {
            ValidateSynchronousExecution(head, startedAt, shieldName);
        }
        catch (Exception exception)
        {
            return Outcome<T>.FromException(exception);
        }

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: true, timeProvider, shieldName);
        try
        {
            var pipeline = RunSync(head, state, action, context);
            var outcome = pipeline.IsCompletedSuccessfully
                ? pipeline.Result
                : pipeline.AsTask().GetAwaiter().GetResult();

            RecordExecution(startedAt, context, outcome.IsSuccess);
            return outcome;
        }
        catch (Exception exception)
        {
            RecordExecution(startedAt, context, success: false);
            return Outcome<T>.FromException(exception);
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

        ValidateSynchronousExecution(head, startedAt, shieldName);

        var context = KevlarContext.Rent(cancellationToken, isSynchronous: true, timeProvider, shieldName);
        try
        {
            try
            {
                initializeProperties(state, context.Properties);
            }
            catch
            {
                RecordExecution(startedAt, context, success: false);
                throw;
            }

            var pipeline = RunWithContextSync(head, state, action, context);
            var outcome = pipeline.IsCompletedSuccessfully
                ? pipeline.Result
                : pipeline.AsTask().GetAwaiter().GetResult();

            RecordExecution(startedAt, context, outcome.IsSuccess);
            return outcome.GetResultOrRethrow();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    private static void ValidateSynchronousExecution(
        StrategyNode? head,
        long startedAt,
        string? shieldName)
    {
        if (head?.SynchronousExecutionUnsupportedReason is { } reason)
        {
            RecordExecution(startedAt, shieldName, success: false);
            throw new NotSupportedException(
                $"Synchronous execution does not support {reason}. " +
                "Use ExecuteAsync instead of Execute.");
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
            RecordExecution(startedAt, context, outcome.IsSuccess);
            return outcome.GetResultOrRethrowInternal();
        }
        finally
        {
            KevlarContext.Return(context);
        }
    }

    private static async ValueTask<T> AwaitWithContextAsync<T, TState>(
        ValueTask<Outcome<T>> pipeline,
        KevlarContext context,
        TState state,
        Action<TState, KevlarProperties>? onCompleted,
        long startedAt)
    {
        try
        {
            var outcome = await pipeline.ConfigureAwait(false);
            RecordExecution(startedAt, context, outcome.IsSuccess);
            return outcome.GetResultOrRethrowInternal();
        }
        finally
        {
            NotifyCompleted(onCompleted, state, context.PropertiesForCompletion);
            KevlarContext.Return(context);
        }
    }

    private static async ValueTask<T> AwaitWithParentContextAsync<T>(
        ValueTask<Outcome<T>> pipeline,
        KevlarContext context,
        KevlarContext parentContext,
        long startedAt)
    {
        try
        {
            var outcome = await pipeline.ConfigureAwait(false);
            RecordExecution(startedAt, context.ShieldName, outcome.IsSuccess);
            return outcome.GetResultOrRethrowInternal();
        }
        finally
        {
            ReturnChildContext(context, parentContext);
        }
    }

    private static void ReturnChildContext(KevlarContext context, KevlarContext parentContext)
    {
        try
        {
            context.CopyChangesToParent(parentContext);
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
            RecordExecution(startedAt, context, outcome.IsSuccess);
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

    private static void RecordExecution(long startedAt, KevlarContext context, bool success)
    {
        KevlarMetrics.Duration(startedAt, context.ShieldName, success, context);
        KevlarMetrics.Execution(context.ShieldName, success, context);
    }

    private static void NotifyCompleted<TState>(
        Action<TState, KevlarProperties>? onCompleted,
        TState state,
        KevlarProperties properties)
    {
        try
        {
            onCompleted?.Invoke(state, properties);
        }
        catch
        {
            // Completion observers must not replace the execution outcome.
        }
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

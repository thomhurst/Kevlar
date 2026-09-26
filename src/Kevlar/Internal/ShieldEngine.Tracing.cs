using System.Runtime.CompilerServices;

namespace Kevlar.Internal;

internal static partial class ShieldEngine
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ExecuteWithParentContextSync<T, TState>(
        StrategyNode? head,
        string? shieldName,
        TState state,
        Func<TState, KevlarContext, T> action,
        KevlarContext parentContext)
    {
#if NET8_0_OR_GREATER
        if (KevlarActivities.Enabled)
        {
            return ExecuteWithParentContextSyncTraced(head, shieldName, state, action, parentContext);
        }
#endif
        return ExecuteWithParentContextSyncUntraced(
            head,
            shieldName,
            state,
            action,
            parentContext);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ExecuteWithParentContextSyncTraced<T, TState>(
        StrategyNode? head,
        string? shieldName,
        TState state,
        Func<TState, KevlarContext, T> action,
        KevlarContext parentContext)
    {
        return KevlarActivities.Execute(
            shieldName,
            (head, shieldName, state, action, parentContext),
            static args => ExecuteWithParentContextSyncUntraced(
                args.head,
                args.shieldName,
                args.state,
                args.action,
                args.parentContext));
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<T> ExecuteWithParentContextAsync<T, TState>(
        StrategyNode? head,
        string? shieldName,
        TState state,
        Func<TState, KevlarContext, ValueTask<T>> action,
        KevlarContext parentContext)
    {
#if NET8_0_OR_GREATER
        if (KevlarActivities.Enabled)
        {
            return ExecuteWithParentContextAsyncTraced(head, shieldName, state, action, parentContext);
        }
#endif
        return ExecuteWithParentContextAsyncUntraced(
            head,
            shieldName,
            state,
            action,
            parentContext);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<T> ExecuteWithParentContextAsyncTraced<T, TState>(
        StrategyNode? head,
        string? shieldName,
        TState state,
        Func<TState, KevlarContext, ValueTask<T>> action,
        KevlarContext parentContext)
    {
        return KevlarActivities.ExecuteAsync(
            shieldName,
            (head, shieldName, state, action, parentContext),
            static args => ExecuteWithParentContextAsyncUntraced(
                args.head,
                args.shieldName,
                args.state,
                args.action,
                args.parentContext));
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
#if NET8_0_OR_GREATER
        if (KevlarActivities.Enabled)
        {
            return ExecuteWithContextAsyncCoreTraced(head, timeProvider, shieldName, state, initializeProperties, action, onCompleted, cancellationToken);
        }
#endif
        return ExecuteWithContextAsyncCoreUntraced(
            head,
            timeProvider,
            shieldName,
            state,
            initializeProperties,
            action,
            onCompleted,
            cancellationToken);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<T> ExecuteWithContextAsyncCoreTraced<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, ValueTask<T>> action,
        Action<TState, KevlarProperties>? onCompleted,
        CancellationToken cancellationToken)
    {
        return KevlarActivities.ExecuteAsync(
            shieldName,
            (head, timeProvider, shieldName, state, initializeProperties, action, onCompleted, cancellationToken),
            static args => ExecuteWithContextAsyncCoreUntraced(
                args.head,
                args.timeProvider,
                args.shieldName,
                args.state,
                args.initializeProperties,
                args.action,
                args.onCompleted,
                args.cancellationToken));
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Outcome<T> ExecuteOutcomeSync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, T> action,
        CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        if (KevlarActivities.Enabled)
        {
            return ExecuteOutcomeSyncTraced(head, timeProvider, shieldName, state, action, cancellationToken);
        }
#endif
        return ExecuteOutcomeSyncUntraced(
            head,
            timeProvider,
            shieldName,
            state,
            action,
            cancellationToken);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Outcome<T> ExecuteOutcomeSyncTraced<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, T> action,
        CancellationToken cancellationToken)
    {
        return KevlarActivities.ExecuteOutcome(
            shieldName,
            (head, timeProvider, shieldName, state, action, cancellationToken),
            static args => ExecuteOutcomeSyncUntraced(
                args.head,
                args.timeProvider,
                args.shieldName,
                args.state,
                args.action,
                args.cancellationToken));
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<T> ExecuteAsync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        if (KevlarActivities.Enabled)
        {
            return ExecuteAsyncTraced(head, timeProvider, shieldName, state, action, cancellationToken);
        }
#endif
        return ExecuteAsyncUntraced(
            head,
            timeProvider,
            shieldName,
            state,
            action,
            cancellationToken);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<T> ExecuteAsyncTraced<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        return KevlarActivities.ExecuteAsync(
            shieldName,
            (head, timeProvider, shieldName, state, action, cancellationToken),
            static args => ExecuteAsyncUntraced(
                args.head,
                args.timeProvider,
                args.shieldName,
                args.state,
                args.action,
                args.cancellationToken));
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ExecuteSync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, T> action,
        CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        if (KevlarActivities.Enabled)
        {
            return ExecuteSyncTraced(head, timeProvider, shieldName, state, action, cancellationToken);
        }
#endif
        return ExecuteSyncUntraced(
            head,
            timeProvider,
            shieldName,
            state,
            action,
            cancellationToken);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ExecuteSyncTraced<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, T> action,
        CancellationToken cancellationToken)
    {
        return KevlarActivities.Execute(
            shieldName,
            (head, timeProvider, shieldName, state, action, cancellationToken),
            static args => ExecuteSyncUntraced(
                args.head,
                args.timeProvider,
                args.shieldName,
                args.state,
                args.action,
                args.cancellationToken));
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<Outcome<T>> ExecuteOutcomeAsync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        if (KevlarActivities.Enabled)
        {
            return ExecuteOutcomeAsyncTraced(head, timeProvider, shieldName, state, action, cancellationToken);
        }
#endif
        return ExecuteOutcomeAsyncUntraced(
            head,
            timeProvider,
            shieldName,
            state,
            action,
            cancellationToken);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ValueTask<Outcome<T>> ExecuteOutcomeAsyncTraced<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        return KevlarActivities.ExecuteOutcomeAsync(
            shieldName,
            (head, timeProvider, shieldName, state, action, cancellationToken),
            static args => ExecuteOutcomeAsyncUntraced(
                args.head,
                args.timeProvider,
                args.shieldName,
                args.state,
                args.action,
                args.cancellationToken));
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ExecuteWithContextSync<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, T> action,
        CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        if (KevlarActivities.Enabled)
        {
            return ExecuteWithContextSyncTraced(head, timeProvider, shieldName, state, initializeProperties, action, cancellationToken);
        }
#endif
        return ExecuteWithContextSyncUntraced(
            head,
            timeProvider,
            shieldName,
            state,
            initializeProperties,
            action,
            cancellationToken);
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ExecuteWithContextSyncTraced<T, TState>(
        StrategyNode? head,
        TimeProvider timeProvider,
        string? shieldName,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, T> action,
        CancellationToken cancellationToken)
    {
        return KevlarActivities.Execute(
            shieldName,
            (head, timeProvider, shieldName, state, initializeProperties, action, cancellationToken),
            static args => ExecuteWithContextSyncUntraced(
                args.head,
                args.timeProvider,
                args.shieldName,
                args.state,
                args.initializeProperties,
                args.action,
                args.cancellationToken));
    }
#endif

}

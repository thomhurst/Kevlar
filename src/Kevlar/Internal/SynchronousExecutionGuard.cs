namespace Kevlar.Internal;

/// <summary>
/// Rejects strategy hooks that do not complete synchronously while a shield executes through the
/// synchronous <c>Execute</c> entry points. Kevlar never blocks the calling thread on a hook.
/// </summary>
internal static class SynchronousExecutionGuard
{
    /// <summary>
    /// Throws when <paramref name="pending"/> is still running and the execution is synchronous.
    /// A completed (successful or faulted) hook is left for the caller to observe.
    /// </summary>
    public static void ThrowIfIncomplete(in ValueTask pending, KevlarContext context, string hookName)
    {
        if (pending.IsCompleted || !context.IsSynchronous)
        {
            return;
        }

        Observe(pending.AsTask());
        throw CreateException(context, hookName);
    }

    /// <inheritdoc cref="ThrowIfIncomplete(in ValueTask, KevlarContext, string)"/>
    public static void ThrowIfIncomplete<TResult>(
        in ValueTask<TResult> pending,
        KevlarContext context,
        string hookName)
    {
        if (pending.IsCompleted || !context.IsSynchronous)
        {
            return;
        }

        Observe(pending.AsTask());
        throw CreateException(context, hookName);
    }

    internal static NotSupportedException CreateException(KevlarContext context, string hookName)
    {
        var shield = context.ShieldName is { Length: > 0 } name ? $" on shield '{name}'" : string.Empty;
        return new SynchronousExecutionRejectionException(
            $"Synchronous execution does not support {hookName} completing asynchronously{shield}. " +
            $"{GetAdvice(context.SynchronousExecutionKind)}, or make the callback complete synchronously.");
    }

    internal static string GetAdvice(SynchronousExecutionKind kind) => kind switch
    {
        SynchronousExecutionKind.ExecuteOutcome => "Use ExecuteOutcomeAsync instead of ExecuteOutcome",
        SynchronousExecutionKind.ExecuteWithContext => "Use ExecuteWithContextAsync instead of ExecuteWithContext",
        _ => "Use ExecuteAsync instead of Execute",
    };

    internal static bool IsRejection(Exception exception) =>
        exception is SynchronousExecutionRejectionException;

    private static void Observe(Task abandoned) =>
        _ = abandoned.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed class SynchronousExecutionRejectionException(string message)
        : NotSupportedException(message);
}

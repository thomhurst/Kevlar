namespace Kevlar.Internal;

/// <summary>
/// Invokes strategy hooks. Every hook returns a <see cref="ValueTask"/>; a hook that completes
/// synchronously costs no more than a plain delegate call.
/// </summary>
internal static class CallbackInvoker
{
    /// <summary>
    /// Invokes a notification hook. Hook failures are reported through
    /// <see cref="KevlarDiagnostics.OnCallbackError"/> and never replace the execution outcome.
    /// Under synchronous execution a hook that does not complete synchronously throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    public static ValueTask InvokeAsync<TEvent>(
        Func<TEvent, ValueTask>? callback,
        TEvent callbackEvent,
        CallbackErrorKind kind,
        KevlarContext context,
        string hookName)
    {
        if (callback is null)
        {
            return default;
        }

        ValueTask notification;
        try
        {
            notification = callback(callbackEvent);
        }
        catch (Exception exception)
        {
            KevlarDiagnostics.ReportCallbackError(kind, context, exception, hookName);
            return default;
        }

        if (notification.IsCompletedSuccessfully)
        {
            notification.GetAwaiter().GetResult();
            return default;
        }

        SynchronousExecutionGuard.ThrowIfIncomplete(in notification, context, hookName);
        return AwaitAsync(notification, kind, context, hookName);
    }

    /// <summary>
    /// Invokes a generator hook whose result the strategy needs. Generator failures propagate to
    /// the caller. Under synchronous execution a generator that does not complete synchronously
    /// throws <see cref="NotSupportedException"/>.
    /// </summary>
    public static ValueTask<TResult> InvokeGenerator<TEvent, TResult>(
        Func<TEvent, ValueTask<TResult>> generator,
        TEvent callbackEvent,
        KevlarContext context,
        string hookName)
    {
        var generation = generator(callbackEvent);
        SynchronousExecutionGuard.ThrowIfIncomplete(in generation, context, hookName);
        return generation;
    }

    private static async ValueTask AwaitAsync(
        ValueTask notification,
        CallbackErrorKind kind,
        KevlarContext context,
        string hookName)
    {
        try
        {
            await notification.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            KevlarDiagnostics.ReportCallbackError(kind, context, exception, hookName);
        }
    }
}

namespace Kevlar.Internal;

internal static class CallbackInvoker
{
    public static void Invoke<TEvent>(
        Action<TEvent>? callback,
        TEvent callbackEvent,
        CallbackErrorKind kind,
        KevlarContext context)
    {
        if (callback is null)
        {
            return;
        }

        try
        {
            callback(callbackEvent);
        }
        catch (Exception exception)
        {
            KevlarDiagnostics.ReportCallbackError(kind, context, exception);
        }
    }

    public static ValueTask InvokeAsync<TEvent>(
        Func<TEvent, ValueTask>? callback,
        TEvent callbackEvent,
        CallbackErrorKind kind,
        KevlarContext context)
    {
        if (callback is null)
        {
            return default;
        }

        try
        {
            var notification = callback(callbackEvent);
            if (notification.IsCompletedSuccessfully)
            {
                notification.GetAwaiter().GetResult();
                return default;
            }

            return AwaitAsync(notification, kind, context);
        }
        catch (Exception exception)
        {
            KevlarDiagnostics.ReportCallbackError(kind, context, exception);
            return default;
        }
    }

    private static async ValueTask AwaitAsync(
        ValueTask notification,
        CallbackErrorKind kind,
        KevlarContext context)
    {
        try
        {
            await notification.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            KevlarDiagnostics.ReportCallbackError(kind, context, exception);
        }
    }
}

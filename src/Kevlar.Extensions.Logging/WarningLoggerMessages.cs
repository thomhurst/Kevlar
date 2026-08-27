using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal static partial class WarningLoggerMessages
{
    [LoggerMessage(EventId = 1009, EventName = "AttemptsSuppressed", Level = LogLevel.Warning,
        Message = "Shield {ShieldName} suppressed additional HTTP attempts for unsafe method {RequestMethod} {RequestUri}; opt in with ShieldHttpHandlerOptions.AllowUnsafeMethodReplay or KevlarHttp.GetRequestOptions(request).AllowReplay",
        SkipEnabledCheck = true)]
    public static partial void UnsafeMethodAttemptsSuppressed(
        ILogger logger,
        string? shieldName,
        string? requestMethod,
        string? requestUri);
}

using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal static partial class WarningLoggerMessages
{
    public const string UnsafeMethodAttemptsSuppressedFormat =
        "Shield {ShieldName} suppressed additional HTTP attempts because {SuppressionReason}; " +
        "unsafe method {RequestMethod} {RequestUri}; opt in with " +
        "ShieldHttpHandlerOptions.AllowUnsafeMethodReplay or " +
        "KevlarHttp.GetRequestOptions(request).AllowReplay";

    [LoggerMessage(EventId = 1009, EventName = "AttemptsSuppressed", Level = LogLevel.Warning,
        Message = UnsafeMethodAttemptsSuppressedFormat,
        SkipEnabledCheck = true)]
    public static partial void UnsafeMethodAttemptsSuppressed(
        ILogger logger,
        string? shieldName,
        string? suppressionReason,
        string? requestMethod,
        string? requestUri);
}

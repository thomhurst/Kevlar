using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal static partial class DebugLoggerMessages
{
    [LoggerMessage(EventId = 1011, EventName = "HedgeAttempt", Level = LogLevel.Debug,
        Message = "Shield {ShieldName} strategy {StrategyIndex} hedge attempt {AttemptNumber} completed after {Duration}; winner {IsWinner}; cancelled {IsCancelled}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void HedgeAttempt(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        int attemptNumber,
        bool isWinner,
        bool isCancelled,
        TimeSpan duration,
        string outcome,
        Exception? exception);
}

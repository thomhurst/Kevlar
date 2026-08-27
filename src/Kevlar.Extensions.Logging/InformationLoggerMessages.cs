using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal static partial class InformationLoggerMessages
{
    [LoggerMessage(EventId = 1011, EventName = "HedgeAttempt", Level = LogLevel.Information,
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

    [LoggerMessage(EventId = 1003, EventName = "CircuitState", Level = LogLevel.Information,
        Message = "Shield {ShieldName} strategy {StrategyIndex} circuit changed from {FromState} to {ToState}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void CircuitStateUntimed(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        CircuitState? fromState,
        CircuitState? toState,
        string outcome,
        Exception? exception);
}

using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal static partial class InformationLoggerMessages
{
    [LoggerMessage(EventId = 1003, EventName = "CircuitState", Level = LogLevel.Information,
        Message = "Shield {ShieldName} strategy {StrategyIndex} circuit changed from {FromState} to {ToState} for {BreakDuration}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void CircuitState(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        CircuitState? fromState,
        CircuitState? toState,
        TimeSpan breakDuration,
        string outcome,
        Exception? exception);
}

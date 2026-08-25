using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal static partial class UntimedLoggerMessages
{
    [LoggerMessage(EventId = 1003, EventName = "CircuitState", Level = LogLevel.Error,
        Message = "Shield {ShieldName} strategy {StrategyIndex} circuit changed from {FromState} to {ToState}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void CircuitState(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        CircuitState? fromState,
        CircuitState? toState,
        string outcome,
        Exception? exception);
}

using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal static partial class LoggerMessages
{
    public static readonly Func<ILogger, string?, IDisposable?> BeginShieldScope =
        LoggerMessage.DefineScope<string?>("Kevlar shield {ShieldName}");

    [LoggerMessage(
        EventId = 1001,
        EventName = "Retry",
        Level = LogLevel.Warning,
        Message = "Shield {ShieldName} strategy {StrategyIndex} retry attempt {Attempt} after {Delay}; outcome {Outcome}; request {RequestMethod} {RequestUri}",
        SkipEnabledCheck = true)]
    public static partial void Retry(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        int attempt,
        TimeSpan delay,
        string outcome,
        string? requestMethod,
        string? requestUri,
        Exception? exception);

    [LoggerMessage(EventId = 1002, EventName = "Timeout", Level = LogLevel.Warning,
        Message = "Shield {ShieldName} strategy {StrategyIndex} timed out after {Duration}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void Timeout(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        TimeSpan duration,
        string outcome,
        Exception? exception);

    [LoggerMessage(EventId = 1003, EventName = "CircuitState", Level = LogLevel.Error,
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

    [LoggerMessage(EventId = 1003, EventName = "CircuitRejected", Level = LogLevel.Error,
        Message = "Shield {ShieldName} strategy {StrategyIndex} rejected attempt {Attempt} because the circuit is {CircuitState}; retry after {RetryAfter}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void CircuitRejected(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        int attempt,
        CircuitState circuitState,
        TimeSpan? retryAfter,
        string outcome,
        Exception? exception);

    [LoggerMessage(EventId = 1004, EventName = "Hedge", Level = LogLevel.Information,
        Message = "Shield {ShieldName} strategy {StrategyIndex} started hedge attempt {Attempt} after {Delay}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void Hedge(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        int attempt,
        TimeSpan delay,
        string outcome,
        Exception? exception);

    [LoggerMessage(EventId = 1005, EventName = "Fallback", Level = LogLevel.Warning,
        Message = "Shield {ShieldName} strategy {StrategyIndex} used fallback on attempt {Attempt}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void Fallback(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        int attempt,
        string outcome,
        Exception? exception);

    [LoggerMessage(EventId = 1006, EventName = "RateLimitRejected", Level = LogLevel.Warning,
        Message = "Shield {ShieldName} strategy {StrategyIndex} rejected attempt {Attempt} by rate limit; retry after {RetryAfter}; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void RateLimitRejected(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        int attempt,
        TimeSpan? retryAfter,
        string outcome,
        Exception? exception);

    [LoggerMessage(EventId = 1007, EventName = "ConcurrencyLimitRejected", Level = LogLevel.Warning,
        Message = "Shield {ShieldName} strategy {StrategyIndex} rejected attempt {Attempt} by concurrency limit; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void ConcurrencyLimitRejected(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        int attempt,
        string outcome,
        Exception? exception);

    [LoggerMessage(EventId = 1008, EventName = "CallbackError", Level = LogLevel.Error,
        Message = "Shield {ShieldName} strategy {StrategyIndex} callback {CallbackKind} ({CallbackSource}) failed; outcome {Outcome}",
        SkipEnabledCheck = true)]
    public static partial void CallbackError(
        ILogger logger,
        string? shieldName,
        int strategyIndex,
        CallbackErrorKind? callbackKind,
        string? callbackSource,
        string outcome,
        Exception? exception);

    [LoggerMessage(EventId = 1009, EventName = "AttemptsSuppressed", Level = LogLevel.Information,
        Message = "Shield {ShieldName} suppressed additional HTTP attempts because {SuppressionReason}; request {RequestMethod} {RequestUri}",
        SkipEnabledCheck = true)]
    public static partial void AttemptsSuppressed(
        ILogger logger,
        string? shieldName,
        string? suppressionReason,
        string? requestMethod,
        string? requestUri);
}

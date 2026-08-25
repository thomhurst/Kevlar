using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal sealed class LoggingTelemetryListener(LoggingRegistration registration)
    : IKevlarTelemetryListener, IKevlarResultTelemetryListener
{
    bool IKevlarResultTelemetryListener.ShouldCaptureResult
    {
        get
        {
            for (var current = registration; current is not null; current = current.Next)
            {
                if (!current.Options.CanAcquire())
                {
                    continue;
                }

                try
                {
                    for (var level = LogLevel.Trace; level < LogLevel.None; level++)
                    {
                        if (current.Logger.IsEnabled(level))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void OnEvent(in KevlarTelemetryEvent telemetryEvent)
    {
        if (!TryMap(in telemetryEvent, out var kind, out var eventId, out var defaultLevel))
        {
            return;
        }

        for (var current = registration; current is not null; current = current.Next)
        {
            try
            {
                Log(current, kind, eventId, defaultLevel, in telemetryEvent);
            }
            catch (Exception exception)
            {
                ReportLoggingFailure(in telemetryEvent, exception);
            }
        }
    }

    private static void Log(
        LoggingRegistration registration,
        KevlarLogEventKind kind,
        EventId eventId,
        LogLevel defaultLevel,
        in KevlarTelemetryEvent telemetryEvent)
    {
        var logEvent = new KevlarLogEvent(kind, in telemetryEvent);
        LogLevel level;
        try
        {
            level = registration.Options.SeverityProvider?.Invoke(logEvent) ?? defaultLevel;
        }
        catch (Exception severityException)
        {
            ReportLoggingFailure(in telemetryEvent, severityException);
            return;
        }

        var logger = registration.Logger;
        if (level == LogLevel.None
            || !logger.IsEnabled(level)
            || !registration.Options.TryAcquire())
        {
            return;
        }

        var outcome = FormatOutcome(registration, kind, in telemetryEvent);
        if (level == defaultLevel)
        {
            LogDefault(logger, kind, outcome, in telemetryEvent);
            return;
        }

        LogOverride(logger, kind, eventId, level, outcome, in telemetryEvent);
    }

    private static void ReportLoggingFailure(
        in KevlarTelemetryEvent telemetryEvent,
        Exception exception)
    {
        if (telemetryEvent.CallbackKind != CallbackErrorKind.Logging)
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.Logging,
                telemetryEvent.Context,
                exception);
        }
    }

    private static string FormatOutcome(
        LoggingRegistration registration,
        KevlarLogEventKind kind,
        in KevlarTelemetryEvent telemetryEvent)
    {
        if (kind == KevlarLogEventKind.CircuitState && telemetryEvent.IsSuccess)
        {
            return "success";
        }

        if (telemetryEvent.Exception is { } exception)
        {
            return exception.GetType().FullName ?? exception.GetType().Name;
        }

        if (kind is not (KevlarLogEventKind.Retry or KevlarLogEventKind.Fallback))
        {
            return telemetryEvent.IsSuccess ? "success" : "failure";
        }

        if (registration.Options.ResultFormatter is not { } formatter)
        {
            return telemetryEvent.Result?.GetType().FullName ?? "success";
        }

        try
        {
            return formatter(telemetryEvent.Result) ?? string.Empty;
        }
        catch (Exception formatterException)
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.Logging,
                telemetryEvent.Context,
                formatterException);
            return "<formatter-error>";
        }
    }

    private static void LogDefault(
        ILogger logger,
        KevlarLogEventKind kind,
        string outcome,
        in KevlarTelemetryEvent telemetryEvent)
    {
        switch (kind)
        {
            case KevlarLogEventKind.Retry:
                _ = telemetryEvent.Context.Properties.TryGet(
                    KevlarKeys.HttpRequestMethod,
                    out string? requestMethod);
                _ = telemetryEvent.Context.Properties.TryGet(
                    KevlarKeys.HttpRequestUri,
                    out string? requestUri);
                LoggerMessages.Retry(logger, telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, telemetryEvent.Delay, outcome, requestMethod, requestUri,
                    telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.Timeout:
                LoggerMessages.Timeout(logger, telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.Duration, outcome, telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.CircuitState:
                if (telemetryEvent.ToState == CircuitState.Open)
                {
                    LoggerMessages.CircuitState(logger, telemetryEvent.ShieldName,
                        telemetryEvent.StrategyIndex, telemetryEvent.FromState, telemetryEvent.ToState,
                        telemetryEvent.Delay, outcome, telemetryEvent.Exception);
                }
                else if (telemetryEvent.ToState is CircuitState.HalfOpen or CircuitState.Closed)
                {
                    InformationLoggerMessages.CircuitStateUntimed(logger, telemetryEvent.ShieldName,
                        telemetryEvent.StrategyIndex, telemetryEvent.FromState, telemetryEvent.ToState,
                        outcome, telemetryEvent.Exception);
                }
                else
                {
                    UntimedLoggerMessages.CircuitState(logger, telemetryEvent.ShieldName,
                        telemetryEvent.StrategyIndex, telemetryEvent.FromState, telemetryEvent.ToState,
                        outcome, telemetryEvent.Exception);
                }
                break;
            case KevlarLogEventKind.CircuitRejected:
            {
                LoggerMessages.CircuitRejected(logger, telemetryEvent.ShieldName,
                    telemetryEvent.StrategyIndex, telemetryEvent.AttemptNumber,
                    CircuitStateFromRejection(in telemetryEvent), telemetryEvent.RetryAfter,
                    outcome, telemetryEvent.Exception);
                break;
            }
            case KevlarLogEventKind.Hedge:
                LoggerMessages.Hedge(logger, telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, outcome, telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.Fallback:
                LoggerMessages.Fallback(logger, telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, outcome, telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.RateLimitRejected:
                LoggerMessages.RateLimitRejected(logger, telemetryEvent.ShieldName,
                    telemetryEvent.StrategyIndex, telemetryEvent.AttemptNumber,
                    telemetryEvent.RetryAfter, outcome, telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.ConcurrencyLimitRejected:
                LoggerMessages.ConcurrencyLimitRejected(logger, telemetryEvent.ShieldName,
                    telemetryEvent.StrategyIndex, telemetryEvent.AttemptNumber, outcome,
                    telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.CallbackError:
                LoggerMessages.CallbackError(logger, telemetryEvent.ShieldName,
                    telemetryEvent.StrategyIndex, telemetryEvent.CallbackKind, outcome,
                    telemetryEvent.Exception);
                break;
        }
    }

    private static void LogOverride(
        ILogger logger,
        KevlarLogEventKind kind,
        EventId eventId,
        LogLevel level,
        string outcome,
        in KevlarTelemetryEvent telemetryEvent)
    {
        switch (kind)
        {
            case KevlarLogEventKind.Retry:
                _ = telemetryEvent.Context.Properties.TryGet(
                    KevlarKeys.HttpRequestMethod,
                    out string? requestMethod);
                _ = telemetryEvent.Context.Properties.TryGet(
                    KevlarKeys.HttpRequestUri,
                    out string? requestUri);
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} retry attempt {Attempt} after {Delay}; outcome {Outcome}; request {RequestMethod} {RequestUri}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, telemetryEvent.Delay, outcome,
                    requestMethod, requestUri);
                break;
            case KevlarLogEventKind.Timeout:
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} timed out after {Duration}; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.Duration, outcome);
                break;
            case KevlarLogEventKind.CircuitState:
                if (telemetryEvent.ToState == CircuitState.Open)
                {
                    logger.Log(level, eventId, telemetryEvent.Exception,
                        "Shield {ShieldName} strategy {StrategyIndex} circuit changed from {FromState} to {ToState} for {BreakDuration}; outcome {Outcome}",
                        telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                        telemetryEvent.FromState, telemetryEvent.ToState, telemetryEvent.Delay, outcome);
                }
                else
                {
                    logger.Log(level, eventId, telemetryEvent.Exception,
                        "Shield {ShieldName} strategy {StrategyIndex} circuit changed from {FromState} to {ToState}; outcome {Outcome}",
                        telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                        telemetryEvent.FromState, telemetryEvent.ToState, outcome);
                }
                break;
            case KevlarLogEventKind.CircuitRejected:
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} rejected attempt {Attempt} because the circuit is {CircuitState}; retry after {RetryAfter}; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, CircuitStateFromRejection(in telemetryEvent),
                    telemetryEvent.RetryAfter, outcome);
                break;
            case KevlarLogEventKind.Hedge:
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} started hedge attempt {Attempt}; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, outcome);
                break;
            case KevlarLogEventKind.Fallback:
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} used fallback on attempt {Attempt}; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, outcome);
                break;
            case KevlarLogEventKind.RateLimitRejected:
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} rejected attempt {Attempt} by rate limit; retry after {RetryAfter}; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, telemetryEvent.RetryAfter, outcome);
                break;
            case KevlarLogEventKind.ConcurrencyLimitRejected:
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} rejected attempt {Attempt} by concurrency limit; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, outcome);
                break;
            case KevlarLogEventKind.CallbackError:
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} callback {CallbackKind} failed; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.CallbackKind, outcome);
                break;
        }
    }

    private static CircuitState CircuitStateFromRejection(
        in KevlarTelemetryEvent telemetryEvent) =>
        telemetryEvent.Exception is CircuitOpenException { IsIsolated: true }
            ? CircuitState.Isolated
            : CircuitState.Open;

    private static bool TryMap(
        in KevlarTelemetryEvent telemetryEvent,
        out KevlarLogEventKind kind,
        out EventId eventId,
        out LogLevel level)
    {
        switch (telemetryEvent.EventName)
        {
            case "retry":
                kind = KevlarLogEventKind.Retry;
                eventId = new EventId(1001, "Retry");
                level = LogLevel.Warning;
                return true;
            case "timeout":
                kind = KevlarLogEventKind.Timeout;
                eventId = new EventId(1002, "Timeout");
                level = LogLevel.Warning;
                return true;
            case "circuit_opened":
            case "circuit_isolated":
                kind = KevlarLogEventKind.CircuitState;
                eventId = new EventId(1003, "CircuitState");
                level = LogLevel.Error;
                return true;
            case "circuit_half_opened":
            case "circuit_closed":
                kind = KevlarLogEventKind.CircuitState;
                eventId = new EventId(1003, "CircuitState");
                level = LogLevel.Information;
                return true;
            case "hedge":
                kind = KevlarLogEventKind.Hedge;
                eventId = new EventId(1004, "Hedge");
                level = LogLevel.Information;
                return true;
            case "fallback":
                kind = KevlarLogEventKind.Fallback;
                eventId = new EventId(1005, "Fallback");
                level = LogLevel.Warning;
                return true;
            case "callback_error":
                kind = KevlarLogEventKind.CallbackError;
                eventId = new EventId(1008, "CallbackError");
                level = LogLevel.Error;
                return true;
            case "rejection" when telemetryEvent.RejectionKind is "rate_limit" or "rate_limiter_adapter":
                kind = KevlarLogEventKind.RateLimitRejected;
                eventId = new EventId(1006, "RateLimitRejected");
                level = LogLevel.Warning;
                return true;
            case "rejection" when telemetryEvent.RejectionKind == "concurrency_limit":
                kind = KevlarLogEventKind.ConcurrencyLimitRejected;
                eventId = new EventId(1007, "ConcurrencyLimitRejected");
                level = LogLevel.Warning;
                return true;
            case "rejection" when telemetryEvent.RejectionKind == "circuit_open":
                kind = KevlarLogEventKind.CircuitRejected;
                eventId = new EventId(1003, "CircuitRejected");
                level = LogLevel.Error;
                return true;
            default:
                kind = default;
                eventId = default;
                level = default;
                return false;
        }
    }
}

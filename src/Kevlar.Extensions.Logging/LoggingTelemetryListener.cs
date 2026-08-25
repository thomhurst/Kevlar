using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal sealed class LoggingTelemetryListener(LoggingRegistration registration)
    : IKevlarTelemetryListener
{
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
                if (telemetryEvent.ToState is CircuitState.HalfOpen or CircuitState.Closed)
                {
                    InformationLoggerMessages.CircuitState(logger, telemetryEvent.ShieldName,
                        telemetryEvent.StrategyIndex, telemetryEvent.FromState, telemetryEvent.ToState,
                        telemetryEvent.Delay, outcome, telemetryEvent.Exception);
                }
                else
                {
                    LoggerMessages.CircuitState(logger, telemetryEvent.ShieldName,
                        telemetryEvent.StrategyIndex, telemetryEvent.FromState, telemetryEvent.ToState,
                        telemetryEvent.Delay, outcome, telemetryEvent.Exception);
                }
                break;
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
        logger.Log(
            level,
            eventId,
            telemetryEvent.Exception,
            "Shield {ShieldName} strategy {StrategyIndex} emitted {EventKind} on attempt {Attempt}; outcome {Outcome}",
            telemetryEvent.ShieldName,
            telemetryEvent.StrategyIndex,
            kind,
            telemetryEvent.AttemptNumber,
            outcome);
    }

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
            case "rejection" when telemetryEvent.StrategyName is "RateLimit" or "RateLimiterAdapter":
                kind = KevlarLogEventKind.RateLimitRejected;
                eventId = new EventId(1006, "RateLimitRejected");
                level = LogLevel.Warning;
                return true;
            case "rejection" when telemetryEvent.StrategyName == "ConcurrencyLimit":
                kind = KevlarLogEventKind.ConcurrencyLimitRejected;
                eventId = new EventId(1007, "ConcurrencyLimitRejected");
                level = LogLevel.Warning;
                return true;
            case "rejection" when telemetryEvent.StrategyName == "CircuitBreaker":
                kind = KevlarLogEventKind.CircuitState;
                eventId = new EventId(1003, "CircuitState");
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

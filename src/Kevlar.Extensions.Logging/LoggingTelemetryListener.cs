using Microsoft.Extensions.Logging;

namespace Kevlar.Extensions.Logging;

internal sealed class LoggingTelemetryListener(LoggingRegistration registration)
    : IKevlarTelemetryListener, IKevlarResultTelemetryListener
{
    private static readonly KevlarKey<string> HttpRequestMethodKey =
        new("kevlar.http.request.method");
    private static readonly KevlarKey<string> HttpRequestUriKey =
        new("kevlar.http.request.uri");

    void IKevlarResultTelemetryListener.OnResultEvent<T>(
        in KevlarTelemetryEvent telemetryEvent,
        in T result)
    {
        if (!TryMap(in telemetryEvent, out var kind, out var eventId, out var defaultLevel))
        {
            return;
        }

        for (var current = registration; current is not null; current = current.Next)
        {
            try
            {
                LogResult(current, kind, eventId, defaultLevel, in telemetryEvent, in result);
            }
            catch (Exception exception)
            {
                ReportLoggingFailure(in telemetryEvent, exception);
            }
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

        Write(registration, kind, eventId, defaultLevel, level, in telemetryEvent);
    }

    private static void LogResult<T>(
        LoggingRegistration registration,
        KevlarLogEventKind kind,
        EventId eventId,
        LogLevel defaultLevel,
        in KevlarTelemetryEvent telemetryEvent,
        in T result)
    {
        var logger = registration.Logger;
        var options = registration.Options;
        var level = defaultLevel;
        if (options.CanEvaluateSeverityWithoutResult)
        {
            var preview = new KevlarLogEvent(kind, in telemetryEvent);
            try
            {
                level = options.SeverityProvider?.Invoke(preview) ?? defaultLevel;
            }
            catch (Exception severityException)
            {
                ReportLoggingFailure(in telemetryEvent, severityException);
                return;
            }

            if (level == LogLevel.None || !logger.IsEnabled(level))
            {
                return;
            }
        }
        else if (!AnyLevelEnabled(logger))
        {
            return;
        }

        if (!options.TryReserve(out var reservation))
        {
            return;
        }

        var resultEvent = telemetryEvent.WithResult(result);
        if (!options.CanEvaluateSeverityWithoutResult)
        {
            try
            {
                level = options.SeverityProvider?.Invoke(new KevlarLogEvent(kind, in resultEvent))
                    ?? defaultLevel;
            }
            catch (Exception severityException)
            {
                options.ReleaseReservation(reservation);
                ReportLoggingFailure(in telemetryEvent, severityException);
                return;
            }

            if (level == LogLevel.None || !logger.IsEnabled(level))
            {
                options.ReleaseReservation(reservation);
                return;
            }
        }

        Write(registration, kind, eventId, defaultLevel, level, in resultEvent);
    }

    private static bool AnyLevelEnabled(ILogger logger)
    {
        for (var level = LogLevel.Trace; level < LogLevel.None; level++)
        {
            if (logger.IsEnabled(level))
            {
                return true;
            }
        }

        return false;
    }

    private static void Write(
        LoggingRegistration registration,
        KevlarLogEventKind kind,
        EventId eventId,
        LogLevel defaultLevel,
        LogLevel level,
        in KevlarTelemetryEvent telemetryEvent)
    {
        var logger = registration.Logger;
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
        if (!string.Equals(
                telemetryEvent.CallbackSource,
                "Kevlar.Extensions.Logging",
                StringComparison.Ordinal))
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.Custom,
                telemetryEvent.Context,
                exception,
                "Kevlar.Extensions.Logging");
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
                CallbackErrorKind.Custom,
                telemetryEvent.Context,
                formatterException,
                "Kevlar.Extensions.Logging");
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
                    HttpRequestMethodKey,
                    out string? requestMethod);
                _ = telemetryEvent.Context.Properties.TryGet(
                    HttpRequestUriKey,
                    out string? requestUri);
                LoggerMessages.Retry(logger, telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, telemetryEvent.Delay, outcome, requestMethod, requestUri,
                    telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.Timeout:
                LoggerMessages.Timeout(logger, telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.Duration, outcome, telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.TimeoutIgnored:
                LoggerMessages.TimeoutIgnored(logger, telemetryEvent.ShieldName,
                    telemetryEvent.StrategyIndex, telemetryEvent.Duration, outcome,
                    telemetryEvent.Exception);
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
                    telemetryEvent.AttemptNumber, telemetryEvent.Delay, outcome, telemetryEvent.Exception);
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
                    telemetryEvent.StrategyIndex, telemetryEvent.CallbackKind,
                    telemetryEvent.CallbackSource, outcome, telemetryEvent.Exception);
                break;
            case KevlarLogEventKind.AttemptsSuppressed:
                _ = telemetryEvent.Context.Properties.TryGet(
                    HttpRequestMethodKey,
                    out string? suppressedRequestMethod);
                _ = telemetryEvent.Context.Properties.TryGet(
                    HttpRequestUriKey,
                    out string? suppressedRequestUri);
                LoggerMessages.AttemptsSuppressed(
                    logger,
                    telemetryEvent.ShieldName,
                    telemetryEvent.SuppressionReason,
                    suppressedRequestMethod,
                    suppressedRequestUri);
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
                    HttpRequestMethodKey,
                    out string? requestMethod);
                _ = telemetryEvent.Context.Properties.TryGet(
                    HttpRequestUriKey,
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
            case KevlarLogEventKind.TimeoutIgnored:
                logger.Log(level, eventId, telemetryEvent.Exception,
                    "Shield {ShieldName} strategy {StrategyIndex} completed after ignoring timeout cancellation for {Elapsed}; outcome {Outcome}",
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
                    "Shield {ShieldName} strategy {StrategyIndex} started hedge attempt {Attempt} after {Delay}; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.AttemptNumber, telemetryEvent.Delay, outcome);
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
                    "Shield {ShieldName} strategy {StrategyIndex} callback {CallbackKind} ({CallbackSource}) failed; outcome {Outcome}",
                    telemetryEvent.ShieldName, telemetryEvent.StrategyIndex,
                    telemetryEvent.CallbackKind, telemetryEvent.CallbackSource, outcome);
                break;
            case KevlarLogEventKind.AttemptsSuppressed:
                _ = telemetryEvent.Context.Properties.TryGet(
                    HttpRequestMethodKey,
                    out string? suppressedRequestMethod);
                _ = telemetryEvent.Context.Properties.TryGet(
                    HttpRequestUriKey,
                    out string? suppressedRequestUri);
                logger.Log(level, eventId,
                    "Shield {ShieldName} suppressed additional HTTP attempts because {SuppressionReason}; request {RequestMethod} {RequestUri}",
                    telemetryEvent.ShieldName, telemetryEvent.SuppressionReason,
                    suppressedRequestMethod, suppressedRequestUri);
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
            case "timeout_ignored":
                kind = KevlarLogEventKind.TimeoutIgnored;
                eventId = new EventId(1010, "TimeoutIgnored");
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
            case "attempts_suppressed":
                kind = KevlarLogEventKind.AttemptsSuppressed;
                eventId = new EventId(1009, "AttemptsSuppressed");
                level = LogLevel.Information;
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

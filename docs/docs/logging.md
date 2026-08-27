---
sidebar_position: 14
---

# Structured logging

`Kevlar.Extensions.Logging` maps built-in strategy events to stable `ILogger` events without
replacing your strategy callbacks.

```shell
dotnet add package Kevlar.Extensions.Logging
```

## Log one shield

```csharp
using Kevlar;
using Kevlar.Extensions.Logging;

var loggedShield = Shield
    .Retry(3)
    .CircuitBreaker(consecutiveFailures: 5, breakDuration: TimeSpan.FromSeconds(30))
    .WithName("catalog")
    .WithLogging(logger, options =>
    {
        options.IncludeScopes = true;
        options.MaxLogsPerSecond = 100;
        options.ResultFormatter = static result => result?.ToString();
        options.SeverityProvider = static logEvent =>
            logEvent.Kind == KevlarLogEventKind.Hedge
                ? LogLevel.Debug
                : logEvent.Kind == KevlarLogEventKind.Retry
                    ? LogLevel.Warning
                    : LogLevel.Information;
    });
```

Return `LogLevel.None` from `SeverityProvider` to suppress an event. Suppression happens before
`ResultFormatter`, so disabled events do not format results. `MaxLogsPerSecond` bounds each logging
configuration independently with a one-second monotonic window.

## Register logging once

`AddKevlarLogging` decorates named, reloading, partitioned, and `HttpClientFactory` shields created
through Kevlar's integration packages:

```csharp
using Kevlar.Extensions.Logging;

services.AddKevlarLogging(options =>
{
    options.IncludeScopes = true;
    options.MaxLogsPerSecond = 500;
});
```

Call it before or after shield registrations. It uses the `Kevlar` logger category from the
registered `ILoggerFactory`. Explicit `WithLogging` calls remain local to that shield.

## Event IDs and levels

| EventId | Event | Default level |
|---:|---|---|
| 1001 | retry | Warning |
| 1002 | timeout | Warning |
| 1003 | circuit state or rejection | Error when opened, isolated, or rejected; Information when half-opened or closed |
| 1004 | hedge | Information |
| 1005 | fallback | Warning |
| 1006 | rate-limit rejection | Warning |
| 1007 | concurrency-limit rejection | Warning |
| 1008 | callback error | Error |
| 1009 | HTTP attempts suppressed | Information |
| 1010 | timeout cancellation ignored | Warning |

Structured state includes the applicable subset of `ShieldName`, `StrategyIndex`, `Attempt`,
`Delay`, `Duration`, `Elapsed`, `Outcome`, `FromState`, `ToState`, `RetryAfter`, `CallbackKind`, and
`SuppressionReason`. HTTP retry and suppression events also include `RequestMethod` and `RequestUri`;
the URI omits query and fragment data.

Logger, formatter, severity-policy, and scope-disposal exceptions never change shield outcomes.
They are reported through `KevlarDiagnostics.ReportCallbackError` with
`CallbackErrorKind.Custom` with source `Kevlar.Extensions.Logging`.

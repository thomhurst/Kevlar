using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Kevlar.Extensions.Logging;

/// <summary>Controls how Kevlar strategy events are written to an <see cref="ILogger"/>.</summary>
public sealed class KevlarLoggingOptions
{
    /// <summary>Overrides the default level for an event. Return <see cref="LogLevel.None"/> to suppress it.</summary>
    public Func<KevlarLogEvent, LogLevel>? SeverityProvider { get; set; }

    /// <summary>Formats a handled result for the structured <c>Outcome</c> field.</summary>
    public Func<object?, string?>? ResultFormatter { get; set; }

    /// <summary>Gets or sets whether executions create a structured shield-name scope.</summary>
    public bool IncludeScopes { get; set; }

    /// <summary>Gets or sets an optional per-configuration log limit per one-second window.</summary>
    public int? MaxLogsPerSecond { get; set; }

    internal LoggingOptionsSnapshot Snapshot()
    {
        if (MaxLogsPerSecond is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxLogsPerSecond),
                MaxLogsPerSecond,
                "MaxLogsPerSecond must not be negative.");
        }

        return new LoggingOptionsSnapshot(
            SeverityProvider,
            ResultFormatter,
            IncludeScopes,
            MaxLogsPerSecond);
    }
}

internal sealed class LoggingOptionsSnapshot(
    Func<KevlarLogEvent, LogLevel>? severityProvider,
    Func<object?, string?>? resultFormatter,
    bool includeScopes,
    int? maxLogsPerSecond)
{
    private readonly object _rateLock = new();
    private long _windowStarted;
    private int _windowCount;

    public Func<KevlarLogEvent, LogLevel>? SeverityProvider { get; } = severityProvider;

    public Func<object?, string?>? ResultFormatter { get; } = resultFormatter;

    public bool IncludeScopes { get; } = includeScopes;

    public int? MaxLogsPerSecond { get; } = maxLogsPerSecond;

    public bool CanAcquire()
    {
        if (MaxLogsPerSecond is not { } limit)
        {
            return true;
        }

        var now = Stopwatch.GetTimestamp();
        lock (_rateLock)
        {
            return limit > 0
                && (_windowStarted == 0
                    || now - _windowStarted >= Stopwatch.Frequency
                    || _windowCount < limit);
        }
    }

    public bool TryAcquire()
    {
        if (MaxLogsPerSecond is not { } limit)
        {
            return true;
        }

        var now = Stopwatch.GetTimestamp();
        lock (_rateLock)
        {
            if (_windowStarted == 0 || now - _windowStarted >= Stopwatch.Frequency)
            {
                _windowStarted = now;
                _windowCount = 0;
            }

            if (_windowCount >= limit)
            {
                return false;
            }

            _windowCount++;
            return true;
        }
    }
}

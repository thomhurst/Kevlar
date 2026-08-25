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

    public bool CanEvaluateSeverityWithoutResult { get; } =
        CanEvaluateWithoutEvent(severityProvider);

    public Func<object?, string?>? ResultFormatter { get; } = resultFormatter;

    public bool IncludeScopes { get; } = includeScopes;

    public int? MaxLogsPerSecond { get; } = maxLogsPerSecond;

    public bool TryReserve(out long windowStarted)
    {
        if (MaxLogsPerSecond is not { } limit)
        {
            windowStarted = 0;
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
                windowStarted = 0;
                return false;
            }

            _windowCount++;
            windowStarted = _windowStarted;
            return true;
        }
    }

    public bool TryAcquire() => TryReserve(out _);

    public void ReleaseReservation(long windowStarted)
    {
        if (MaxLogsPerSecond is null)
        {
            return;
        }

        lock (_rateLock)
        {
            if (_windowStarted == windowStarted && _windowCount > 0)
            {
                _windowCount--;
            }
        }
    }

#if NET8_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Missing or altered method bodies conservatively retain result capture.")]
#endif
    private static bool CanEvaluateWithoutEvent(
        Func<KevlarLogEvent, LogLevel>? severityProvider)
    {
        if (severityProvider is null)
        {
            return true;
        }

#if NET8_0_OR_GREATER
        try
        {
            var body = severityProvider.Method.GetMethodBody()?.GetILAsByteArray();
            if (body is null)
            {
                return false;
            }

            var argumentIndex = severityProvider.Method.IsStatic ? 0 : 1;
            var shortLoad = (byte)(0x02 + argumentIndex);
            for (var index = 0; index < body.Length; index++)
            {
                if (body[index] == shortLoad
                    || body[index] is 0x0E or 0x0F
                        && index + 1 < body.Length
                        && body[index + 1] == argumentIndex
                    || body[index] == 0xFE
                        && index + 3 < body.Length
                        && body[index + 1] is 0x09 or 0x0A
                        && body[index + 2] == argumentIndex
                        && body[index + 3] == 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
#else
        return false;
#endif
    }
}

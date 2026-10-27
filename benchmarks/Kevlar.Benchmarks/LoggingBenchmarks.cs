using BenchmarkDotNet.Attributes;
using Kevlar.Extensions.Logging;
using Microsoft.Extensions.Logging;

namespace Kevlar.Benchmarks;

/// <summary>Measures result-based retry logging with and without a severity provider.</summary>
[MemoryDiagnoser]
public class LoggingBenchmarks
{
    private Shield<int> _shield = null!;

    [Params(false, true)]
    public bool SeverityProviderEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _shield = Shield.For<int>()
            .WhenResultEquals(-1)
            .Retry(1, Backoff.None)
            .WithLogging(EnabledLogger.Instance, options =>
            {
                if (SeverityProviderEnabled)
                {
                    // Keep fixtures compatible with the baseline before DefaultLevel was added.
                    options.SeverityProvider = static logEvent =>
                        logEvent.Kind == KevlarLogEventKind.Retry
                            ? LogLevel.Warning
                            : LogLevel.Information;
                }
            });
    }

    [Benchmark]
    public ValueTask<int> ResultRetry() => _shield.ExecuteAsync(static _ => new ValueTask<int>(-1));

    private sealed class EnabledLogger : ILogger
    {
        public static readonly EnabledLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}

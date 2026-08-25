using System.Collections.Concurrent;
using Kevlar.Extensions.DependencyInjection;
using Kevlar.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Kevlar.IntegrationTests;

public class LoggingIntegrationTests
{
    [Test]
    [NotInParallel]
    public async Task OpenTelemetry_Export_Carries_ShieldName()
    {
        var exporter = new CollectingExporter();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddOpenTelemetry(options =>
        {
            options.ParseStateValues = true;
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        services.AddKevlarLogging();
        services.AddShield("otel", Shield.Retry(1, Backoff.None));
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IKevlarRegistry>();

        _ = await registry.GetShield("otel").ExecuteOutcomeAsync<int>(static _ =>
            new ValueTask<int>(Task.FromException<int>(new TestException("failure"))));

        await Assert.That(exporter.ShieldNames).Contains("otel");
    }

    private sealed class CollectingExporter : BaseExporter<LogRecord>
    {
        public ConcurrentQueue<string?> ShieldNames { get; } = new();

        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
            {
                ShieldNames.Enqueue(record.Attributes?
                    .FirstOrDefault(pair => pair.Key == "ShieldName")
                    .Value
                    ?.ToString());
            }

            return ExportResult.Success;
        }
    }

    private sealed class TestException(string message) : Exception(message);
}

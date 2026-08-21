using System.Diagnostics;
using System.Runtime.InteropServices;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Kevlar.StressTests;

internal static class StressRunner
{
    private static readonly Shield KevlarShield = Shield
        .Timeout(TimeSpan.FromSeconds(10))
        .Retry(3, Backoff.None)
        .CircuitBreaker(5, TimeSpan.FromSeconds(30));

    private static readonly ResiliencePipeline PollyPipeline = new ResiliencePipelineBuilder()
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.Zero })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions())
        .Build();

    public static async Task<StressRunResult> RunAsync(StressOptions options)
    {
        var phaseDuration = TimeSpan.FromTicks(options.Duration.Ticks / 2);
        Console.WriteLine(
            $"Running {options.Workers} workers for {phaseDuration:g} per library " +
            $"({options.Duration:g} total measured time).");

        await WarmUpAsync("Polly", ExecutePollyAsync, options);
        await WarmUpAsync("Kevlar", ExecuteKevlarAsync, options);

        var results = new[]
        {
            await MeasureAsync("Polly", ExecutePollyAsync, phaseDuration, options.Workers),
            await MeasureAsync("Kevlar", ExecuteKevlarAsync, phaseDuration, options.Workers),
        };

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new StressRunResult(
            DateTimeOffset.UtcNow,
            Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount,
            options.Workers,
            options.Duration,
            options.Warmup,
            process.PeakWorkingSet64,
            results);
    }

    private static async Task WarmUpAsync(
        string library,
        Func<ValueTask<int>> operation,
        StressOptions options)
    {
        if (options.Warmup == TimeSpan.Zero)
        {
            return;
        }

        Console.WriteLine($"Warming {library} for {options.Warmup:g}...");
        await RunWorkersAsync(operation, options.Warmup, options.Workers);
    }

    private static async Task<StressPhaseResult> MeasureAsync(
        string library,
        Func<ValueTask<int>> operation,
        TimeSpan duration,
        int workers)
    {
        ForceCollection();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        Console.WriteLine($"Measuring {library} for {duration:g}...");
        var stopwatch = Stopwatch.StartNew();
        var operations = await RunWorkersAsync(operation, duration, workers);
        stopwatch.Stop();
        if (operations == 0)
        {
            throw new InvalidOperationException($"{library} completed no operations.");
        }

        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var result = new StressPhaseResult(
            library,
            operations,
            stopwatch.Elapsed.TotalSeconds,
            operations / stopwatch.Elapsed.TotalSeconds,
            allocatedBytes,
            allocatedBytes / (double)operations,
            managedBefore,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before);

        Console.WriteLine(
            $"{library}: {result.OperationsPerSecond:N0} ops/s, " +
            $"{result.BytesPerOperation:N2} B/op, {result.AllocatedBytes / 1_048_576d:N1} MiB allocated.");
        return result;
    }

    private static async Task<long> RunWorkersAsync(
        Func<ValueTask<int>> operation,
        TimeSpan duration,
        int workerCount)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var workers = new Task<long>[workerCount];
        for (var worker = 0; worker < workers.Length; worker++)
        {
            workers[worker] = Task.Run(async () =>
            {
                var operations = 0L;
                while (Stopwatch.GetTimestamp() < deadline)
                {
                    var result = await operation().ConfigureAwait(false);
                    if (result != 42)
                    {
                        throw new InvalidOperationException($"Workload returned {result}; expected 42.");
                    }

                    operations++;
                }

                return operations;
            });
        }

        return (await Task.WhenAll(workers)).Sum();
    }

    private static void ForceCollection()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static ValueTask<int> ExecuteKevlarAsync() =>
        KevlarShield.ExecuteAsync(static _ => new ValueTask<int>(42));

    private static ValueTask<int> ExecutePollyAsync() =>
        PollyPipeline.ExecuteAsync(static _ => new ValueTask<int>(42));
}

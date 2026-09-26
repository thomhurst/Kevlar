using System.Diagnostics;
using System.Runtime.InteropServices;
using Kevlar.Testing;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Kevlar.StressTests;

internal static class StressRunner
{
    private const int MeasurementRounds = 4;
    private const int PartitionExecutions = 1_000_000;
    private const int PartitionKeys = 10_000;
    private const int MaxPartitions = 1_000;

    private static readonly Shield KevlarSharedRatioPipeline = CreateKevlarRatioPipeline();
    private static readonly Shield KevlarTimeoutRetryPipeline = CreateKevlarTimeoutRetryPipeline();
    private static readonly ResiliencePipeline PollySharedRatioPipeline = CreatePollyRatioPipeline();
    private static readonly ResiliencePipeline PollyTimeoutRetryPipeline = CreatePollyTimeoutRetryPipeline();

    private static Shield CreateKevlarTimeoutRetryPipeline() => Shield
        .Timeout(TimeSpan.FromSeconds(10))
        .Retry(3, Backoff.None);

    private static Shield CreateKevlarRatioPipeline() => CreateKevlarTimeoutRetryPipeline()
        .CircuitBreaker(ConfigureKevlarRatioBreaker);

    private static void ConfigureKevlarRatioBreaker(CircuitBreakerOptions options)
    {
        options.FailureRatio = 0.1;
        options.MinimumThroughput = 100;
        options.SamplingWindow = TimeSpan.FromSeconds(30);
        options.BreakDuration = TimeSpan.FromSeconds(5);
    }

    private static ResiliencePipelineBuilder CreatePollyTimeoutRetryBuilder() => new ResiliencePipelineBuilder()
        .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromSeconds(10) })
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.Zero });

    private static ResiliencePipeline CreatePollyTimeoutRetryPipeline() =>
        CreatePollyTimeoutRetryBuilder().Build();

    private static ResiliencePipeline CreatePollyRatioPipeline() =>
        CreatePollyTimeoutRetryBuilder()
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.1,
            MinimumThroughput = 100,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(5),
        })
        .Build();

    public static async Task<StressRunResult> RunAsync(StressOptions options)
    {
        var cases = CreateCases(options.Workers);
        var phaseDuration = TimeSpan.FromTicks(
            options.Duration.Ticks / (MeasurementRounds * cases.Count * 2));
        if (phaseDuration == TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Duration is too short for the stress scenario matrix.");
        }

        Console.WriteLine(
            $"Running {cases.Count} scenarios for {MeasurementRounds} alternating rounds " +
            $"of {phaseDuration:g} per library and scenario ({options.Duration:g} total measured time).");

        foreach (var stressCase in cases)
        {
            await WarmUpAsync(stressCase, "Polly", stressCase.PollyOperations, options.Warmup);
            await WarmUpAsync(stressCase, "Kevlar", stressCase.KevlarOperations, options.Warmup);
        }

        await RunPartitionStressAsync();
        await RunAdaptiveConcurrencyStressAsync(options.Workers);

        var phases = new List<StressPhaseResult>(MeasurementRounds * cases.Count * 2);
        foreach (var stressCase in cases)
        {
            for (var round = 0; round < MeasurementRounds; round++)
            {
                if (round % 2 == 0)
                {
                    phases.Add(await MeasureAsync(stressCase, "Polly", stressCase.PollyOperations, phaseDuration, round));
                    phases.Add(await MeasureAsync(stressCase, "Kevlar", stressCase.KevlarOperations, phaseDuration, round));
                }
                else
                {
                    phases.Add(await MeasureAsync(stressCase, "Kevlar", stressCase.KevlarOperations, phaseDuration, round));
                    phases.Add(await MeasureAsync(stressCase, "Polly", stressCase.PollyOperations, phaseDuration, round));
                }
            }
        }

        var results = cases
            .SelectMany(stressCase => new[]
            {
                Aggregate(stressCase, "Polly", phases),
                Aggregate(stressCase, "Kevlar", phases),
            })
            .ToArray();
        foreach (var result in results)
        {
            Console.WriteLine(
                $"{result.Scenario} ({result.Workers} workers), {result.Library}: " +
                $"{result.OperationsPerSecond:N0} ops/s, " +
                $"{result.BytesPerOperation:N2} B/op, " +
                $"{result.GcPauseSeconds * 1_000:N1} ms GC pause, " +
                $"{result.LockContentions:N0} lock contentions.");
        }

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
            MeasurementRounds,
            process.PeakWorkingSet64,
            results);
    }

    private static async Task WarmUpAsync(
        StressCase stressCase,
        string library,
        Func<ValueTask<int>>[] operations,
        TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
        {
            return;
        }

        Console.WriteLine(
            $"Warming {stressCase.Name} ({stressCase.Workers} workers), {library}, for {duration:g}...");
        await RunWorkersAsync(operations, duration);
    }

    private static async Task<StressPhaseResult> MeasureAsync(
        StressCase stressCase,
        string library,
        Func<ValueTask<int>>[] operations,
        TimeSpan duration,
        int round)
    {
        ForceCollection();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        var gcPauseBefore = GC.GetTotalPauseDuration();
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var lockContentionsBefore = Monitor.LockContentionCount;

        Console.WriteLine(
            $"Measuring {stressCase.Name} ({stressCase.Workers} workers), {library}, " +
            $"round {round + 1}/{MeasurementRounds}, for {duration:g}...");
        var stopwatch = Stopwatch.StartNew();
        var operationCount = await RunWorkersAsync(operations, duration);
        stopwatch.Stop();
        if (operationCount == 0)
        {
            throw new InvalidOperationException(
                $"{stressCase.Name}, {library} completed no operations.");
        }

        process.Refresh();
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var result = new StressPhaseResult(
            stressCase.Name,
            library,
            stressCase.Workers,
            operationCount,
            stopwatch.Elapsed.TotalSeconds,
            operationCount / stopwatch.Elapsed.TotalSeconds,
            (process.TotalProcessorTime - cpuBefore).TotalSeconds,
            allocatedBytes,
            allocatedBytes / (double)operationCount,
            managedBefore,
            GC.GetTotalMemory(forceFullCollection: false),
            (GC.GetTotalPauseDuration() - gcPauseBefore).TotalSeconds,
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before,
            Monitor.LockContentionCount - lockContentionsBefore);

        Console.WriteLine(
            $"{stressCase.Name}, {library} round {round + 1}: " +
            $"{result.OperationsPerSecond:N0} ops/s, {result.BytesPerOperation:N2} B/op, " +
            $"{result.GcPauseSeconds * 1_000:N1} ms GC pause, " +
            $"{result.LockContentions:N0} lock contentions.");
        return result;
    }

    private static StressPhaseResult Aggregate(
        StressCase stressCase,
        string library,
        List<StressPhaseResult> phases)
    {
        var matching = phases
            .Where(result => result.Scenario == stressCase.Name
                && result.Workers == stressCase.Workers
                && result.Library == library)
            .ToArray();
        var operations = matching.Sum(result => result.Operations);
        var elapsedSeconds = matching.Sum(result => result.ElapsedSeconds);
        var allocatedBytes = matching.Sum(result => result.AllocatedBytes);

        return new StressPhaseResult(
            stressCase.Name,
            library,
            stressCase.Workers,
            operations,
            elapsedSeconds,
            operations / elapsedSeconds,
            matching.Sum(result => result.CpuSeconds),
            allocatedBytes,
            allocatedBytes / (double)operations,
            matching[0].ManagedBytesBefore,
            matching[^1].ManagedBytesAfter,
            matching.Sum(result => result.GcPauseSeconds),
            matching.Sum(result => result.Gen0Collections),
            matching.Sum(result => result.Gen1Collections),
            matching.Sum(result => result.Gen2Collections),
            matching.Sum(result => result.LockContentions));
    }

    private static async Task<long> RunWorkersAsync(
        Func<ValueTask<int>>[] operations,
        TimeSpan duration)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var workers = new Task<long>[operations.Length];
        for (var worker = 0; worker < workers.Length; worker++)
        {
            var operation = operations[worker];
            workers[worker] = Task.Run(async () =>
            {
                var operationCount = 0L;
                while (Stopwatch.GetTimestamp() < deadline)
                {
                    var result = await operation().ConfigureAwait(false);
                    if (result != 42)
                    {
                        throw new InvalidOperationException($"Workload returned {result}; expected 42.");
                    }

                    operationCount++;
                }

                return operationCount;
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

    private static async Task RunPartitionStressAsync()
    {
        Console.WriteLine(
            $"Stress-testing {PartitionExecutions:N0} partition executions across " +
            $"{PartitionKeys:N0} keys with a {MaxPartitions:N0}-partition bound...");
        var partitions = PartitionedShield<int>.CreateAsync(
            static key => new ValueTask<Shield>(Shield.Retry(key & 1, Backoff.None)),
            new PartitionedShieldOptions<int> { MaxPartitions = MaxPartitions });

        for (var operation = 0; operation < PartitionExecutions; operation++)
        {
            var shield = await partitions.GetShieldAsync(operation % PartitionKeys).ConfigureAwait(false);
            var result = await shield.ExecuteAsync(static _ => new ValueTask<int>(42)).ConfigureAwait(false);
            if (result != 42)
            {
                throw new InvalidOperationException($"Partition workload returned {result}; expected 42.");
            }
        }

        if (partitions.Count > MaxPartitions)
        {
            throw new InvalidOperationException(
                $"Partition retention exceeded its bound: {partitions.Count} > {MaxPartitions}.");
        }

        Console.WriteLine(
            $"Partition stress complete: {partitions.Count:N0} retained, " +
            $"{partitions.EvictionCount:N0} evicted.");
    }

    private static async Task RunAdaptiveConcurrencyStressAsync(int workers)
    {
        var maximum = Math.Max(2, workers);
        var shield = Shield.ConcurrencyLimit(new AdaptiveConcurrencyLimitOptions
        {
            InitialLimit = maximum,
            MaxLimit = maximum,
            SamplingWindow = TimeSpan.FromMilliseconds(5),
            DecreaseFactor = 0.5,
        });
        await VerifyAdaptiveFeedbackAsync(shield, maximum);
        var active = 0;
        var peak = 0;
        var completed = 0;
        var rejected = 0;
        var failed = 0;
        var fault = new IOException("Injected adaptive-concurrency stress failure.");
        var runs = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            for (var operation = 0; operation < 512; operation++)
            {
                var outcome = await shield.ExecuteOutcomeAsync<int>(async _ =>
                {
                    var running = Interlocked.Increment(ref active);
                    try
                    {
                        int observed;
                        do
                        {
                            observed = Volatile.Read(ref peak);
                        }
                        while (running > observed && Interlocked.CompareExchange(ref peak, running, observed) != observed);
                        await Task.Yield();
                        if (Interlocked.Increment(ref completed) % 19 == 0)
                        {
                            throw fault;
                        }
                        return 42;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                });
                if (outcome.Exception is ConcurrencyLimitExceededException)
                {
                    Interlocked.Increment(ref rejected);
                    await Task.Yield();
                }
                else if (ReferenceEquals(outcome.Exception, fault))
                {
                    Interlocked.Increment(ref failed);
                }
                else if (!outcome.IsSuccess || outcome.Result != 42)
                {
                    throw new InvalidOperationException("Adaptive concurrency stress returned an unexpected outcome.");
                }
            }
        })).ToArray();
        await Task.WhenAll(runs);
        var snapshot = shield.GetStateSnapshot().Strategies.OfType<ConcurrencyLimitStateSnapshot>().Single();
        if (peak > maximum || active != 0 || snapshot.RunningExecutions != 0
            || snapshot.CurrentLimit < 1 || snapshot.CurrentLimit > maximum
            || completed + rejected != workers * 512)
        {
            throw new InvalidOperationException("Adaptive concurrency stress violated permit or outcome accounting.");
        }
        Console.WriteLine($"Adaptive concurrency stress: {completed} admitted, {rejected} rejected, " +
            $"{failed} injected failures, peak {peak}, final limit {snapshot.CurrentLimit}.");
    }

    private static async Task VerifyAdaptiveFeedbackAsync(Shield shield, int initialLimit)
    {
        var time = new FakeTimeProvider();
        var timed = shield.WithTimeProvider(time);
        _ = timed.Execute(static _ => 42);
        var gates = Enumerable.Range(0, initialLimit)
            .Select(_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var executions = gates.Select(gate =>
            timed.ExecuteOutcomeAsync(_ => new ValueTask<int>(gate.Task)).AsTask()).ToArray();
        try
        {
            var excess = await timed.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
            if (excess.Exception is not ConcurrencyLimitExceededException)
            {
                throw new InvalidOperationException("Adaptive stress did not reject excess synchronized admission.");
            }

            time.Advance(TimeSpan.FromMilliseconds(5));
            gates[0].SetException(new IOException("Injected adaptive feedback failure."));
            _ = await executions[0];
            var snapshot = shield.GetStateSnapshot().Strategies.OfType<ConcurrencyLimitStateSnapshot>().Single();
            if (snapshot.CurrentLimit != Math.Max(1, initialLimit / 2)
                || snapshot.RunningExecutions != initialLimit - 1)
            {
                throw new InvalidOperationException("Adaptive stress did not reduce its limit while preserving running calls.");
            }
        }
        finally
        {
            foreach (var gate in gates)
            {
                gate.TrySetResult(42);
            }
            _ = await Task.WhenAll(executions);
        }
        Console.WriteLine("Adaptive feedback verified: synchronized rejection, failure backoff, and permit drain.");
    }

    private static IReadOnlyList<StressCase> CreateCases(int workers)
    {
        var cases = new List<StressCase>
        {
            CreateSharedRatioCase(workers: 1),
        };

        if (workers > 1)
        {
            cases.Add(CreateSharedRatioCase(workers));
        }

        cases.Add(CreateTimeoutRetryCase(workers));
        if (workers > 1)
        {
            cases.Add(CreatePerWorkerRatioCase(workers));
        }

        return cases;
    }

    private static StressCase CreateSharedRatioCase(int workers) => new(
        "SharedRatioPipeline",
        workers,
        Repeat(workers, CreateKevlarOperation(KevlarSharedRatioPipeline)),
        Repeat(workers, CreatePollyOperation(PollySharedRatioPipeline)));

    private static StressCase CreateTimeoutRetryCase(int workers) => new(
        "TimeoutRetryPipeline",
        workers,
        Repeat(workers, CreateKevlarOperation(KevlarTimeoutRetryPipeline)),
        Repeat(workers, CreatePollyOperation(PollyTimeoutRetryPipeline)));

    private static StressCase CreatePerWorkerRatioCase(int workers)
    {
        var kevlarOperations = Enumerable.Range(0, workers)
            .Select(_ => CreateKevlarOperation(CreateKevlarRatioPipeline()))
            .ToArray();
        var pollyOperations = Enumerable.Range(0, workers)
            .Select(_ => CreatePollyOperation(CreatePollyRatioPipeline()))
            .ToArray();

        return new StressCase("PerWorkerRatioPipeline", workers, kevlarOperations, pollyOperations);
    }

    private static Func<ValueTask<int>>[] Repeat(int count, Func<ValueTask<int>> operation) =>
        Enumerable.Repeat(operation, count).ToArray();

    private static Func<ValueTask<int>> CreateKevlarOperation(Shield pipeline) =>
        () => pipeline.ExecuteAsync(static _ => new ValueTask<int>(42));

    private static Func<ValueTask<int>> CreatePollyOperation(ResiliencePipeline pipeline) =>
        () => pipeline.ExecuteAsync(static _ => new ValueTask<int>(42));

    private sealed record StressCase(
        string Name,
        int Workers,
        Func<ValueTask<int>>[] KevlarOperations,
        Func<ValueTask<int>>[] PollyOperations);
}

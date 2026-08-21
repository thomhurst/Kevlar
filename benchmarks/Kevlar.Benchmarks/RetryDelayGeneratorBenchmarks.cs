using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>Measures retry delay-generation modes when one retry is required.</summary>
[MemoryDiagnoser]
public class RetryDelayGeneratorBenchmarks
{
    private static readonly InvalidOperationException RecoverableError = new("transient");

    private static readonly Shield FixedDelay = CreateShield();
    private static readonly Shield SynchronousGenerator = CreateShield(options =>
        options.DelayGenerator = static _ => TimeSpan.Zero);
    private static readonly Shield CompletedAsyncGenerator = CreateShield(options =>
        options.DelayGeneratorAsync = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero));
    private static readonly Shield YieldingAsyncGenerator = CreateShield(options =>
        options.DelayGeneratorAsync = YieldAsync);

    private readonly Counter _fixedCounter = new();
    private readonly Counter _synchronousCounter = new();
    private readonly Counter _completedAsyncCounter = new();
    private readonly Counter _yieldingAsyncCounter = new();

    [Benchmark(Baseline = true)]
    public ValueTask<int> Fixed() => ExecuteAsync(FixedDelay, _fixedCounter);

    [Benchmark]
    public ValueTask<int> Synchronous() => ExecuteAsync(SynchronousGenerator, _synchronousCounter);

    [Benchmark]
    public ValueTask<int> AsyncCompleted() => ExecuteAsync(CompletedAsyncGenerator, _completedAsyncCounter);

    [Benchmark]
    public ValueTask<int> AsyncYielding() => ExecuteAsync(YieldingAsyncGenerator, _yieldingAsyncCounter);

    private static Shield CreateShield(Action<RetryOptions>? configure = null) => Shield.Retry(options =>
    {
        options.MaxRetries = 1;
        options.Backoff = Backoff.None;
        configure?.Invoke(options);
    });

    private static ValueTask<int> ExecuteAsync(Shield shield, Counter counter) =>
        shield.ExecuteAsync(counter, static (state, _) =>
        {
            if (++state.Value % 2 != 0)
            {
                throw RecoverableError;
            }

            return new ValueTask<int>(42);
        });

    private static async ValueTask<TimeSpan?> YieldAsync(RetryEvent _)
    {
        await Task.Yield();
        return TimeSpan.Zero;
    }

    private sealed class Counter
    {
        public int Value;
    }
}

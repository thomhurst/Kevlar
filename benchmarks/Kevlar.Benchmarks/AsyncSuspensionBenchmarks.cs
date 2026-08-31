using System.Threading.Tasks.Sources;
using BenchmarkDotNet.Attributes;

namespace Kevlar.Benchmarks;

/// <summary>
/// Measures strategy execution when user work suspends once without allocating its own
/// task or state machine. The reusable source completes only after Kevlar has returned
/// the incomplete execution to this benchmark.
/// </summary>
[MemoryDiagnoser]
public class AsyncSuspensionBenchmarks
{
    private static readonly Shield Empty = Shield.Empty;
    private static readonly Shield Retry = Shield.Retry(0, Backoff.None);
    private static readonly Shield CircuitBreaker = Shield.CircuitBreaker(
        consecutiveFailures: 5,
        breakDuration: TimeSpan.FromMinutes(1));
    private static readonly Shield Timeout = Shield.Timeout(TimeSpan.FromMinutes(1));
    private static readonly Shield RateLimit = Shield.RateLimit(
        1_000_000_000,
        perWindow: TimeSpan.FromSeconds(1));
    private static readonly Shield ConcurrencyLimit = Shield.ConcurrencyLimit(1024);
    private static readonly Shield<int> Fallback = Shield.For<int>().FallbackTo(7);
    private static readonly Shield<int> Hedge = Shield.For<int>().Hedge(
        1,
        delay: TimeSpan.FromMinutes(1));

    private readonly PendingValueTaskSource _source = new();

    [Benchmark(Baseline = true)]
    public ValueTask<int> EmptyShield() => Execute(Empty);

    [Benchmark]
    public ValueTask<int> RetryShield() => Execute(Retry);

    [Benchmark]
    public ValueTask<int> CircuitBreakerShield() => Execute(CircuitBreaker);

    [Benchmark]
    public ValueTask<int> TimeoutShield() => Execute(Timeout);

    [Benchmark]
    public ValueTask<int> RateLimitShield() => Execute(RateLimit);

    [Benchmark]
    public Task<int> RateLimitShieldAsTask() => ExecuteAsTask(RateLimit);

    [Benchmark]
    public ValueTask<int> ConcurrencyLimitShield() => Execute(ConcurrencyLimit);

    [Benchmark]
    public ValueTask<int> FallbackShield() => Execute(Fallback);

    [Benchmark]
    public ValueTask<int> HedgeShield() => Execute(Hedge);

    private ValueTask<int> Execute(Shield shield)
    {
        var pending = _source.Prepare();
        var execution = shield.ExecuteAsync(pending, static (operation, _) => operation);
        _source.Complete();
        return execution;
    }

    private ValueTask<int> Execute(Shield<int> shield)
    {
        var pending = _source.Prepare();
        var execution = shield.ExecuteAsync(pending, static (operation, _) => operation);
        _source.Complete();
        return execution;
    }

    private Task<int> ExecuteAsTask(Shield shield)
    {
        var pending = _source.Prepare();
        var execution = shield.ExecuteAsync(pending, static (operation, _) => operation).AsTask();
        _source.Complete();
        return execution;
    }

    private sealed class PendingValueTaskSource : IValueTaskSource<int>
    {
        private ManualResetValueTaskSourceCore<int> _source;

        public ValueTask<int> Prepare()
        {
            _source.Reset();
            return new ValueTask<int>(this, _source.Version);
        }

        public void Complete() => _source.SetResult(42);

        public int GetResult(short token) => _source.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);
    }
}

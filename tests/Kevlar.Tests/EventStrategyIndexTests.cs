using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class EventStrategyIndexTests
{
    [Test]
    public async Task All_Events_Expose_StrategyIndex_Via_Context()
    {
        var observed = new Dictionary<string, int>(StringComparer.Ordinal);

        var retry = TypedPrefix()
            .WhenResult(-1)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = item => observed["retry"] = item.Context.StrategyIndex;
            });
        _ = await retry.ExecuteAsync(static _ => new ValueTask<int>(-1));

        var timeout = Prefix().Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(1);
            options.OnTimeout = item => observed["timeout"] = item.Context.StrategyIndex;
        });
        _ = await timeout.ExecuteOutcomeAsync<int>(static async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        });

        var hedge = TypedPrefix().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = item => observed["hedge"] = item.Context.StrategyIndex;
            options.ActionGenerator = item =>
            {
                observed["hedge-generator"] = item.Context.StrategyIndex;
                return null;
            };
        });
        _ = await hedge.ExecuteAsync(static _ => new ValueTask<int>(42));

        var fallback = TypedPrefix()
            .When<InvalidOperationException>()
            .FallbackTo(42, options => options.OnFallback = item =>
                observed["fallback"] = item.Context.StrategyIndex);
        _ = await fallback.ExecuteAsync(static _ => throw new InvalidOperationException());

        var breaker = TypedPrefix()
            .WhenResult(-1)
            .CircuitBreaker(options =>
            {
                options.ConsecutiveFailures = 1;
                options.BreakDurationGenerator = item =>
                {
                    observed["breaker-duration"] = item.Context.StrategyIndex;
                    return new ValueTask<TimeSpan>(TimeSpan.FromMinutes(1));
                };
                options.OnStateChanged = item =>
                    observed["breaker-state"] = item.Context.StrategyIndex;
            });
        _ = await breaker.ExecuteAsync(static _ => new ValueTask<int>(-1));

        var timeProvider = new FakeTimeProvider();
        var rateLimit = Prefix().RateLimit(options =>
            {
                options.Permits = 1;
                options.Window = TimeSpan.FromMinutes(1);
                options.OnRejected = item =>
                    observed["rate-limit"] = item.Context.StrategyIndex;
            })
            .WithTimeProvider(timeProvider);
        await rateLimit.ExecuteAsync(static _ => ValueTask.CompletedTask);
        _ = await rateLimit.ExecuteOutcomeAsync<int>(static _ => new ValueTask<int>(0));

        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrencyLimit = Prefix().ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = 1;
            options.OnRejected = item =>
                observed["concurrency-limit"] = item.Context.StrategyIndex;
        });
        var running = concurrencyLimit.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
        }).AsTask();
        await started.Task;
        _ = await concurrencyLimit.ExecuteOutcomeAsync<int>(static _ => new ValueTask<int>(0));
        release.SetResult();
        await running;

        var expected = new[]
        {
            "retry",
            "timeout",
            "hedge",
            "hedge-generator",
            "fallback",
            "breaker-duration",
            "breaker-state",
            "rate-limit",
            "concurrency-limit",
        };
        await Assert.That(observed.Keys).IsEquivalentTo(expected);
        await Assert.That(observed.Values.All(static index => index == 1)).IsTrue();
    }

    [Test]
    public async Task Parallel_Continuations_Restore_Owning_Strategy_Index()
    {
        var inner = new OutOfOrderCompletionStrategy();
        var outer = new ParallelContinuationStrategy(inner);
        var shield = Shield.Use(outer).Use(inner);

        var outcome = await shield.ExecuteOutcomeAsync<int>(static _ => new ValueTask<int>(42));

        await Assert.That(outcome.Exception).IsNull();
        await Assert.That(outer.StrategyIndexAfterContinuations).IsEqualTo(0);
        await Assert.That(inner.FirstStrategyIndexAfterRelease).IsEqualTo(1);
        await Assert.That(inner.SecondStrategyIndexAfterRelease).IsEqualTo(1);
    }

    private static Shield Prefix() => Shield.Use(new PassThroughStrategy());

    private static Shield<int> TypedPrefix() =>
        Shield<int>.Empty.Use(new PassThroughStrategy());

    private sealed class PassThroughStrategy : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private sealed class ParallelContinuationStrategy(OutOfOrderCompletionStrategy inner) : Strategy
    {
        public int StrategyIndexAfterContinuations { get; private set; } = -1;

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var first = next.InvokeAsync(context).AsTask();
            var second = next.InvokeAsync(context).AsTask();

            await inner.BothEntered.Task;
            inner.ReleaseFirst.SetResult();
            _ = await first;
            inner.ReleaseSecond.SetResult();
            var outcome = await second;

            StrategyIndexAfterContinuations = context.StrategyIndex;
            return outcome;
        }
    }

    private sealed class OutOfOrderCompletionStrategy : Strategy
    {
        private int _invocations;

        public TaskCompletionSource BothEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecond { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int FirstStrategyIndexAfterRelease { get; private set; } = -1;

        public int SecondStrategyIndexAfterRelease { get; private set; } = -1;

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var invocation = Interlocked.Increment(ref _invocations);
            if (invocation == 2)
            {
                BothEntered.SetResult();
            }

            await (invocation == 1 ? ReleaseFirst.Task : ReleaseSecond.Task);
            if (invocation == 1)
            {
                FirstStrategyIndexAfterRelease = context.StrategyIndex;
            }
            else
            {
                SecondStrategyIndexAfterRelease = context.StrategyIndex;
            }

            return await next.InvokeAsync(context);
        }
    }
}

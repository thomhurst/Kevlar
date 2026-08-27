namespace Kevlar.Tests;

/// <summary>
/// Every strategy hook is a single ValueTask-returning delegate. Under synchronous execution a hook
/// that completes synchronously runs inline; a hook that yields is rejected with a
/// <see cref="NotSupportedException"/> naming the hook, and <c>ExecuteOutcome</c> returns that
/// exception as the outcome. The guard is sync-only: <c>ExecuteAsync</c> awaits the same hooks.
/// </summary>
public class SynchronousHookGuardTests
{
    private const string UseExecuteAsync = "Use ExecuteAsync";

    // ---- RetryOptions.OnRetry ----

    [Test]
    public async Task Retry_OnRetry_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ => await gate.Task;
        });

        await AssertRejected(() => shield.Execute(FailOnce()), "RetryOptions.OnRetry");
    }

    [Test]
    public async Task Retry_OnRetry_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ => await gate.Task;
        });

        await AssertRejectedOutcome(shield.ExecuteOutcome(FailOnce()), "RetryOptions.OnRetry");
    }

    [Test]
    public async Task Retry_OnRetry_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ =>
            {
                observed++;
                return default;
            };
        });

        await Assert.That(shield.Execute(FailOnce())).IsEqualTo(42);
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- RetryOptions.DelayGenerator ----

    [Test]
    public async Task Retry_DelayGenerator_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = async _ =>
            {
                await gate.Task;
                return TimeSpan.Zero;
            };
        });

        await AssertRejected(() => shield.Execute(FailOnce()), "RetryOptions.DelayGenerator");
    }

    [Test]
    public async Task Retry_DelayGenerator_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = async _ =>
            {
                await gate.Task;
                return TimeSpan.Zero;
            };
        });

        await AssertRejectedOutcome(shield.ExecuteOutcome(FailOnce()), "RetryOptions.DelayGenerator");
    }

    [Test]
    public async Task Retry_DelayGenerator_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ =>
            {
                observed++;
                return new(TimeSpan.Zero);
            };
        });

        await Assert.That(shield.Execute(FailOnce())).IsEqualTo(42);
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- RetryOptions<TResult>.OnRetry ----

    [Test]
    public async Task Typed_Retry_OnRetry_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.For<int>().WhenResultEquals(-1).Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ => await gate.Task;
        });

        await AssertRejected(() => shield.Execute(NegativeOnce()), "RetryOptions<TResult>.OnRetry");
    }

    [Test]
    public async Task Typed_Retry_OnRetry_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.For<int>().WhenResultEquals(-1).Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ => await gate.Task;
        });

        await AssertRejectedOutcome(shield.ExecuteOutcome(NegativeOnce()), "RetryOptions<TResult>.OnRetry");
    }

    [Test]
    public async Task Typed_Retry_OnRetry_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.For<int>().WhenResultEquals(-1).Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ =>
            {
                observed++;
                return default;
            };
        });

        await Assert.That(shield.Execute(NegativeOnce())).IsEqualTo(42);
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- RetryOptions<TResult>.DelayGenerator ----

    [Test]
    public async Task Typed_Retry_DelayGenerator_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.For<int>().WhenResultEquals(-1).Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = async _ =>
            {
                await gate.Task;
                return TimeSpan.Zero;
            };
        });

        await AssertRejected(() => shield.Execute(NegativeOnce()), "RetryOptions<TResult>.DelayGenerator");
    }

    [Test]
    public async Task Typed_Retry_DelayGenerator_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.For<int>().WhenResultEquals(-1).Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = async _ =>
            {
                await gate.Task;
                return TimeSpan.Zero;
            };
        });

        await AssertRejectedOutcome(
            shield.ExecuteOutcome(NegativeOnce()),
            "RetryOptions<TResult>.DelayGenerator");
    }

    [Test]
    public async Task Typed_Retry_DelayGenerator_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.For<int>().WhenResultEquals(-1).Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ =>
            {
                observed++;
                return new(TimeSpan.FromMilliseconds(1));
            };
        });

        await Assert.That(shield.Execute(NegativeOnce())).IsEqualTo(42);
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- TimeoutOptions.TimeoutGenerator ----

    [Test]
    public async Task Timeout_Generator_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.Timeout(options => options.TimeoutGenerator = async _ =>
        {
            await gate.Task;
            return TimeSpan.FromSeconds(1);
        });

        await AssertRejected(() => shield.Execute(_ => 42), "TimeoutOptions.TimeoutGenerator");
    }

    [Test]
    public async Task Timeout_Generator_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.Timeout(options => options.TimeoutGenerator = async _ =>
        {
            await gate.Task;
            return TimeSpan.FromSeconds(1);
        });

        await AssertRejectedOutcome(shield.ExecuteOutcome(_ => 42), "TimeoutOptions.TimeoutGenerator");
    }

    [Test]
    public async Task Timeout_Generator_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.Timeout(options => options.TimeoutGenerator = _ =>
        {
            observed++;
            return new(TimeSpan.FromSeconds(1));
        });

        await Assert.That(shield.Execute(_ => 42)).IsEqualTo(42);
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- TimeoutOptions.OnTimeout ----

    [Test]
    public async Task Timeout_OnTimeout_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(10);
            options.OnTimeout = async _ => await gate.Task;
        });

        await AssertRejected(() => shield.Execute(WaitForCancellation), "TimeoutOptions.OnTimeout");
    }

    [Test]
    public async Task Timeout_OnTimeout_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(10);
            options.OnTimeout = async _ => await gate.Task;
        });

        await AssertRejectedOutcome(shield.ExecuteOutcome(WaitForCancellation), "TimeoutOptions.OnTimeout");
    }

    [Test]
    public async Task Timeout_OnTimeout_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.Timeout(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(10);
            options.OnTimeout = _ =>
            {
                observed++;
                return default;
            };
        });

        await Assert.That(() => shield.Execute(WaitForCancellation)).Throws<TimeoutExceededException>();
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- CircuitBreakerOptions.OnStateChanged ----

    [Test]
    public async Task Breaker_OnStateChanged_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.OnStateChanged = async _ => await gate.Task;
        });

        await AssertRejected(() => shield.Execute<int>(Fail), "CircuitBreakerOptions.OnStateChanged");
    }

    [Test]
    public async Task Breaker_OnStateChanged_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.OnStateChanged = async _ => await gate.Task;
        });

        await AssertRejectedOutcome(shield.ExecuteOutcome<int>(Fail), "CircuitBreakerOptions.OnStateChanged");
    }

    [Test]
    public async Task Breaker_OnStateChanged_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromMinutes(1);
            options.OnStateChanged = _ =>
            {
                observed++;
                return default;
            };
        });

        await Assert.That(() => shield.Execute<int>(Fail)).Throws<InvalidOperationException>();
        await Assert.That(observed).IsEqualTo(1);
        await Assert.That(() => shield.Execute(_ => 1)).Throws<CircuitOpenException>();
    }

    // ---- CircuitBreakerOptions.BreakDurationGenerator ----

    [Test]
    public async Task Breaker_BreakDurationGenerator_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDurationGenerator = async _ =>
            {
                await gate.Task;
                return TimeSpan.FromMinutes(1);
            };
        });

        await AssertRejected(() => shield.Execute<int>(Fail), "CircuitBreakerOptions.BreakDurationGenerator");
    }

    [Test]
    public async Task Breaker_BreakDurationGenerator_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDurationGenerator = async _ =>
            {
                await gate.Task;
                return TimeSpan.FromMinutes(1);
            };
        });

        await AssertRejectedOutcome(
            shield.ExecuteOutcome<int>(Fail),
            "CircuitBreakerOptions.BreakDurationGenerator");
    }

    [Test]
    public async Task Breaker_BreakDurationGenerator_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDurationGenerator = _ =>
            {
                observed++;
                return new(TimeSpan.FromMinutes(1));
            };
        });

        await Assert.That(() => shield.Execute<int>(Fail)).Throws<InvalidOperationException>();
        await Assert.That(observed).IsEqualTo(1);
        await Assert.That(() => shield.Execute(_ => 1)).Throws<CircuitOpenException>();
    }

    // ---- FallbackOptions.OnFallback (void fallback) ----

    [Test]
    public async Task Void_Fallback_Recovery_Is_Rejected_Statically_Before_OnFallback_Runs()
    {
        // Every void fallback recovery delegate is ValueTask-shaped and stays statically rejected
        // under synchronous execution, so FallbackOptions.OnFallback never gets a chance to run there.
        var observed = 0;
        var shield = Shield.Fallback(
            static _ => default,
            options => options.OnFallback = _ =>
            {
                observed++;
                return default;
            });

        var exception = await Assert.That(() => shield.Execute(_ => throw new InvalidOperationException()))
            .Throws<NotSupportedException>();

        await Assert.That(exception!.Message).Contains("Fallback recovery delegate");
        await Assert.That(exception.Message).DoesNotContain("FallbackOptions.OnFallback");
        await Assert.That(observed).IsEqualTo(0);
    }

    // ---- FallbackOptions<TResult>.OnFallback ----

    [Test]
    public async Task Typed_Fallback_OnFallback_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.For<int>().FallbackTo(
            0,
            options => options.OnFallback = async _ => await gate.Task);

        await AssertRejected(() => shield.Execute(Fail), "FallbackOptions<TResult>.OnFallback");
    }

    [Test]
    public async Task Typed_Fallback_OnFallback_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.For<int>().FallbackTo(
            0,
            options => options.OnFallback = async _ => await gate.Task);

        await AssertRejectedOutcome(shield.ExecuteOutcome(Fail), "FallbackOptions<TResult>.OnFallback");
    }

    [Test]
    public async Task Typed_Fallback_OnFallback_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.For<int>().FallbackTo(
            7,
            options => options.OnFallback = _ =>
            {
                observed++;
                return default;
            });

        await Assert.That(shield.Execute(Fail)).IsEqualTo(7);
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- ConcurrencyLimitOptions.OnRejected ----

    [Test]
    public async Task ConcurrencyLimit_OnRejected_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = 1;
            options.OnRejected = async _ => await gate.Task;
        });

        await using var occupier = await Occupy(shield);

        await AssertRejected(() => shield.Execute(_ => 2), "ConcurrencyLimitOptions.OnRejected");
    }

    [Test]
    public async Task ConcurrencyLimit_OnRejected_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = 1;
            options.OnRejected = async _ => await gate.Task;
        });

        await using var occupier = await Occupy(shield);

        await AssertRejectedOutcome(shield.ExecuteOutcome(_ => 2), "ConcurrencyLimitOptions.OnRejected");
    }

    [Test]
    public async Task ConcurrencyLimit_OnRejected_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = 1;
            options.OnRejected = _ =>
            {
                observed++;
                return default;
            };
        });

        await using var occupier = await Occupy(shield);

        await Assert.That(() => shield.Execute(_ => 2)).Throws<ConcurrencyLimitExceededException>();
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- RateLimitOptions.OnRejected ----

    [Test]
    public async Task RateLimit_OnRejected_That_Yields_Is_Rejected_By_Execute()
    {
        using var gate = new Gate();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromHours(1);
            options.OnRejected = async _ => await gate.Task;
        });
        _ = shield.Execute(_ => 1);

        await AssertRejected(() => shield.Execute(_ => 2), "RateLimitOptions.OnRejected");
    }

    [Test]
    public async Task RateLimit_OnRejected_That_Yields_Fails_ExecuteOutcome()
    {
        using var gate = new Gate();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromHours(1);
            options.OnRejected = async _ => await gate.Task;
        });
        _ = shield.Execute(_ => 1);

        await AssertRejectedOutcome(shield.ExecuteOutcome(_ => 2), "RateLimitOptions.OnRejected");
    }

    [Test]
    public async Task RateLimit_OnRejected_That_Completes_Synchronously_Runs_Under_Execute()
    {
        var observed = 0;
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromHours(1);
            options.OnRejected = _ =>
            {
                observed++;
                return default;
            };
        });
        _ = shield.Execute(_ => 1);

        await Assert.That(() => shield.Execute(_ => 2)).Throws<RateLimitExceededException>();
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- The guard is sync-only ----

    [Test]
    public async Task ExecuteAsync_Awaits_A_Retry_Hook_That_Yields()
    {
        var observed = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ =>
            {
                await Task.Yield();
                observed++;
            };
        });

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ => ++attempts == 1
            ? ValueTask.FromException<int>(new InvalidOperationException())
            : new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observed).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_Awaits_A_Timeout_Generator_That_Yields()
    {
        var observed = 0;
        var shield = Shield.Timeout(options => options.TimeoutGenerator = async _ =>
        {
            await Task.Yield();
            observed++;
            return TimeSpan.FromSeconds(1);
        });

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observed).IsEqualTo(1);
    }

    // ---- helpers ----

    private static async Task AssertRejected(Func<int> execute, string hookName)
    {
        var exception = await Assert.That(execute).Throws<NotSupportedException>();

        await Assert.That(exception!.Message).Contains(hookName);
        await Assert.That(exception.Message).Contains(UseExecuteAsync);
    }

    private static async Task AssertRejectedOutcome(Outcome<int> outcome, string hookName)
    {
        await Assert.That(outcome.IsSuccess).IsFalse();
        await Assert.That(outcome.Exception).IsTypeOf<NotSupportedException>();
        await Assert.That(outcome.Exception!.Message).Contains(hookName);
        await Assert.That(outcome.Exception.Message).Contains(UseExecuteAsync);
    }

    private static Func<CancellationToken, int> FailOnce()
    {
        var attempts = 0;
        return _ => ++attempts == 1 ? throw new InvalidOperationException("first attempt") : 42;
    }

    private static Func<CancellationToken, int> NegativeOnce()
    {
        var attempts = 0;
        return _ => ++attempts == 1 ? -1 : 42;
    }

    private static int Fail(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("boom");

    private static int WaitForCancellation(CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.WaitOne();
        cancellationToken.ThrowIfCancellationRequested();
        return 1;
    }

    private static async Task<Occupier> Occupy(Shield shield)
    {
        var occupier = new Occupier();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        occupier.Execution = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await occupier.Gate.Task;
            return 1;
        }).AsTask();
        await started.Task;
        return occupier;
    }

    private sealed class Occupier : IAsyncDisposable
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> Execution { get; set; } = Task.FromResult(0);

        public async ValueTask DisposeAsync()
        {
            Gate.TrySetResult();
            await Execution;
        }
    }

    /// <summary>
    /// Keeps a hook pending until the test disposes it, so the synchronous guard is guaranteed to
    /// observe an incomplete ValueTask (a bare <c>Task.Yield</c> could finish on the thread pool
    /// before the guard looks). Disposal releases the abandoned hook after the assertions.
    /// </summary>
    private sealed class Gate : IDisposable
    {
        private readonly TaskCompletionSource _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Task => _source.Task;

        public void Dispose() => _source.TrySetResult();
    }
}

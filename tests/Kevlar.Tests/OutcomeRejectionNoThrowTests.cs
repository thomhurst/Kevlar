namespace Kevlar.Tests;

/// <summary>
/// Rejections reported through <c>ExecuteOutcomeAsync</c> must never be thrown and caught
/// inside the pipeline: that throw is the cost the outcome API exists to avoid on hot
/// fast-fail paths. A first-chance exception probe scoped to the current async flow proves it.
/// </summary>
public class OutcomeRejectionNoThrowTests
{
    private static readonly TimeSpan LongWindow = TimeSpan.FromHours(1);

    [Test]
    public async Task Probe_Observes_Throwing_Rejection_Path()
    {
        var shield = await IsolatedBreakerAsync(Shield.Empty);

        using var probe = FirstChanceProbe.Start();
        await Assert.That(async () => await shield.ExecuteAsync(static _ => new ValueTask<int>(42)))
            .Throws<CircuitOpenException>();
        var thrown = probe.Stop();

        await Assert.That(thrown).Contains(nameof(CircuitOpenException));
    }

    [Test]
    public async Task Isolated_Breaker_Outcome_Does_Not_Throw()
    {
        var shield = await IsolatedBreakerAsync(Shield.Empty);

        await AssertRejectedWithoutThrowing<CircuitOpenException>(shield);
    }

    [Test]
    public async Task Timeout_Retry_Isolated_Breaker_Outcome_Does_Not_Throw()
    {
        var shield = await IsolatedBreakerAsync(
            Shield.Timeout(TimeSpan.FromMinutes(1)).Retry(3, Backoff.None));

        await AssertRejectedWithoutThrowing<CircuitOpenException>(shield);
    }

    [Test]
    public async Task Retry_Handling_Rejections_Around_Isolated_Breaker_Does_Not_Throw()
    {
        var shield = await IsolatedBreakerAsync(Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.HandlesException = static exception => exception is CircuitOpenException;
        }));

        await AssertRejectedWithoutThrowing<CircuitOpenException>(shield);
    }

    [Test]
    public async Task Hedge_Around_Isolated_Breaker_Outcome_Does_Not_Throw()
    {
        var shield = await IsolatedBreakerAsync(Shield.Empty.Hedge(1, delay: TimeSpan.FromMinutes(1)));

        await AssertRejectedWithoutThrowing<CircuitOpenException>(shield);
    }

    [Test]
    public async Task Fallback_Substitutes_Isolated_Breaker_Rejection_Without_Throwing()
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.For<int>()
            .FallbackTo(7)
            .CircuitBreaker(options => options.Monitor = monitor);
        await monitor.IsolateAsync();

        Outcome<int> outcome;
        IReadOnlyList<string> thrown;
        using (var probe = FirstChanceProbe.Start())
        {
            outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
            thrown = probe.Stop();
        }

        await Assert.That(outcome.IsSuccess).IsTrue();
        await Assert.That(outcome.Result).IsEqualTo(7);
        await Assert.That(thrown).IsEmpty();
    }

    [Test]
    public async Task Exhausted_Rate_Limit_Outcome_Does_Not_Throw()
    {
        var shield = Shield.Timeout(TimeSpan.FromMinutes(1))
            .Retry(3, Backoff.None)
            .RateLimit(1, perWindow: LongWindow);
        var admitted = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
        await Assert.That(admitted.IsSuccess).IsTrue();

        await AssertRejectedWithoutThrowing<RateLimitExceededException>(shield);
    }

    [Test]
    public async Task Saturated_Concurrency_Limit_Outcome_Does_Not_Throw()
    {
        var shield = Shield.Timeout(TimeSpan.FromMinutes(1))
            .Retry(3, Backoff.None)
            .ConcurrencyLimit(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = shield.ExecuteAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 0;
        });
        await entered.Task;

        try
        {
            await AssertRejectedWithoutThrowing<ConcurrencyLimitExceededException>(shield);
        }
        finally
        {
            release.SetResult();
            await active;
        }
    }

    private static async Task<Shield> IsolatedBreakerAsync(Shield outer)
    {
        var monitor = new CircuitBreakerMonitor();
        var shield = outer.CircuitBreaker(options => options.Monitor = monitor);
        await monitor.IsolateAsync();
        return shield;
    }

    private static async Task AssertRejectedWithoutThrowing<TRejection>(Shield shield)
        where TRejection : Exception
    {
        Outcome<int> outcome;
        IReadOnlyList<string> thrown;
        using (var probe = FirstChanceProbe.Start())
        {
            outcome = await shield.ExecuteOutcomeAsync(static _ => new ValueTask<int>(42));
            thrown = probe.Stop();
        }

        await Assert.That(outcome.Exception).IsTypeOf<TRejection>();
        await Assert.That(thrown).IsEmpty();
    }

    /// <summary>Records first-chance exceptions raised within the async flow that started it.</summary>
    private sealed class FirstChanceProbe : IDisposable
    {
        private static readonly AsyncLocal<FirstChanceProbe?> Current = new();
        private readonly List<string> _thrown = [];
        private bool _stopped;

        private FirstChanceProbe()
        {
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        }

        public static FirstChanceProbe Start()
        {
            var probe = new FirstChanceProbe();
            Current.Value = probe;
            return probe;
        }

        public IReadOnlyList<string> Stop()
        {
            lock (_thrown)
            {
                _stopped = true;
                return [.. _thrown];
            }
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            Current.Value = null;
        }

        private void OnFirstChance(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
        {
            if (!ReferenceEquals(Current.Value, this))
            {
                return;
            }

            lock (_thrown)
            {
                if (!_stopped)
                {
                    _thrown.Add(args.Exception.GetType().Name);
                }
            }
        }
    }
}

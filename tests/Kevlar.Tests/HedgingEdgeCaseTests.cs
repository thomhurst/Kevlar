namespace Kevlar.Tests;

public class HedgingEdgeCaseTests
{
    [Test]
    public async Task OnHedge_Fires_For_Each_Extra_Attempt()
    {
        var hedges = new List<int>();
        var policy = Policy.Hedge(options =>
        {
            options.MaxAttempts = 3;
            options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
            options.OnHedge = hedge => hedges.Add(hedge.Attempt);
        });

        await Assert.That(async () => await policy.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(hedges).IsEquivalentTo([2, 3]);
    }

    [Test]
    public async Task The_Losing_Attempt_Is_Cancelled_When_A_Winner_Completes()
    {
        var loserCancelled = new TaskCompletionSource();
        var attempts = 0;
        var policy = Policy.Hedge(2, TimeSpan.Zero);

        var result = await policy.ExecuteAsync(async token =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                using var registration = token.Register(() => loserCancelled.TrySetResult());
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }

            return "winner";
        });

        await Assert.That(result).IsEqualTo("winner");
        await loserCancelled.Task;
    }

    [Test]
    public async Task Result_Handling_Hedges_On_Bad_Results()
    {
        var attempts = 0;
        var policy = Policy.For<int>()
            .HandleResult(value => value < 0)
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
            });

        var result = await policy.ExecuteAsync(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            return new ValueTask<int>(attempt == 1 ? -1 : 42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task When_Every_Attempt_Produces_A_Handled_Result_The_Last_One_Is_Returned()
    {
        var attempts = 0;
        var policy = Policy.For<int>()
            .HandleResult(value => value < 0)
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
            });

        var result = await policy.ExecuteAsync(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            return new ValueTask<int>(-attempt);
        });

        await Assert.That(result).IsEqualTo(-2);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Caller_Cancellation_Cancels_All_Pending_Attempts()
    {
        using var cancellation = new CancellationTokenSource();
        var started = 0;
        var policy = Policy.Hedge(2, TimeSpan.Zero);

        var task = policy.ExecuteAsync(async token =>
        {
            Interlocked.Increment(ref started);
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token).AsTask();

        await TestHelpers.WaitUntil(() => Volatile.Read(ref started) == 2);
        cancellation.Cancel();

        await Assert.That(async () => await task).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task A_Single_Attempt_Hedge_Runs_Synchronously()
    {
        // MaxAttempts of 1 means no hedging at all, so the synchronous path is allowed.
        var result = Policy.Hedge(1, TimeSpan.FromSeconds(1)).Execute(_ => 5);
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task An_Unhandled_Exception_Wins_Immediately()
    {
        var attempts = 0;
        var policy = Policy
            .Handle<InvalidOperationException>()
            .Hedge(options =>
            {
                options.MaxAttempts = 3;
                options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
            });

        // ArgumentException is not in the handling clause, so hedging stops with it
        // rather than launching further attempts.
        await Assert.That(async () => await policy.ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref attempts);
            throw new ArgumentException("not hedged");
        })).Throws<ArgumentException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Hedged_Attempts_See_The_Callers_Context_Properties()
    {
        var key = new KevlarKey<string>("request-id");
        string? seenOnHedge = null;

        var policy = Policy
            .Use(new PropertySeedingStrategy(key, "abc-123"))
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
                options.OnHedge = hedge => seenOnHedge = hedge.Context.Properties.GetOrDefault(key, "missing");
            });

        var attempts = 0;
        var result = await policy.ExecuteAsync(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                throw new InvalidOperationException();
            }

            return new ValueTask<int>(attempt);
        });

        await Assert.That(result).IsEqualTo(2);
        await Assert.That(seenOnHedge).IsEqualTo("abc-123");
    }

    private sealed class PropertySeedingStrategy : Strategy
    {
        private readonly KevlarKey<string> _key;
        private readonly string _value;

        public PropertySeedingStrategy(KevlarKey<string> key, string value)
        {
            _key = key;
            _value = value;
        }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
        {
            context.Properties.Set(_key, _value);
            return next.InvokeAsync(context);
        }
    }
}

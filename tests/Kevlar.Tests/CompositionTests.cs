namespace Kevlar.Tests;

public class CompositionTests
{
    private sealed class MarkerStrategy : Strategy
    {
        private readonly List<string> _log;
        private readonly string _name;

        public MarkerStrategy(List<string> log, string name)
        {
            _log = log;
            _name = name;
        }

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
        {
            _log.Add($"{_name}:enter");
            var outcome = await next.InvokeAsync(context);
            _log.Add($"{_name}:exit");
            return outcome;
        }
    }

    [Test]
    public async Task First_Strategy_In_The_Chain_Is_Outermost()
    {
        var log = new List<string>();
        var shield = Shield
            .Use(new MarkerStrategy(log, "outer"))
            .Use(new MarkerStrategy(log, "inner"));

        await shield.ExecuteAsync(_ =>
        {
            log.Add("action");
            return new ValueTask<int>(1);
        });

        await Assert.That(log).IsEquivalentTo(["outer:enter", "inner:enter", "action", "inner:exit", "outer:exit"]);
    }

    [Test]
    public async Task Compose_Merges_Policies_In_Order()
    {
        var log = new List<string>();
        var first = Shield.Use(new MarkerStrategy(log, "first"));
        var second = Shield.Use(new MarkerStrategy(log, "second"));

        var composed = Shield.Compose(first, second);

        await composed.ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(log).IsEquivalentTo(["first:enter", "second:enter", "second:exit", "first:exit"]);
    }

    [Test]
    public async Task Wrap_Places_The_Outer_Policy_First()
    {
        var log = new List<string>();
        var outer = Shield.Use(new MarkerStrategy(log, "outer"));
        var inner = Shield.Use(new MarkerStrategy(log, "inner"));

        await outer.Wrap(inner).ExecuteAsync(_ => new ValueTask<int>(1));

        await Assert.That(log).IsEquivalentTo(["outer:enter", "inner:enter", "inner:exit", "outer:exit"]);
    }

    [Test]
    public async Task A_Shared_Circuit_Breaker_Shares_Its_State_Across_Policies()
    {
        var breaker = Shield.CircuitBreaker(1, TimeSpan.FromMinutes(1));
        var policyA = Shield.Timeout(TimeSpan.FromSeconds(30)).Wrap(breaker);
        var policyB = Shield.Retry(0, Backoff.None).Wrap(breaker);

        await Assert.That(async () => await policyA.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        // The same circuit, tripped via shield A, rejects executions through shield B.
        await Assert.That(async () => await policyB.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task NonGeneric_Policy_Wraps_A_Typed_Policy()
    {
        var attempts = 0;
        var typed = Shield.For<string>().When<InvalidOperationException>().Fallback("fallback");
        var combined = Shield.Retry(1, Backoff.None).Wrap(typed);

        // Fallback is inner, so it recovers before the outer retry ever sees a failure.
        var result = await combined.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(result).IsEqualTo("fallback");
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Total_Timeout_Caps_All_Retries()
    {
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var attempts = 0;
        var attemptsStarted = new AsyncCounter("total-timeout attempts");
        var shield = Shield
            .Timeout(TimeSpan.FromSeconds(3))
            .When<InvalidOperationException>()
            .Retry(10, Backoff.Constant(TimeSpan.FromSeconds(1)))
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            attemptsStarted.Signal();
            throw new InvalidOperationException();
        }).AsTask();

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await attemptsStarted.WaitForAsync(2);

        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await attemptsStarted.WaitForAsync(3);

        // The third second trips the outer total timeout while the retry is sleeping.
        fakeTime.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(async () => await task).Throws<TimeoutExceededException>();
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Empty_Policy_Passes_Through()
    {
        var result = await Shield.Empty.ExecuteAsync(_ => new ValueTask<int>(123));
        await Assert.That(result).IsEqualTo(123);
    }

    [Test]
    public async Task Policy_Name_Flows_To_Callbacks()
    {
        string? seenName = null;
        var shield = Shield
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = retry => seenName = retry.Context.ShieldName;
            })
            .WithName("my-shield");

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(seenName).IsEqualTo("my-shield");
    }

    [Test]
    public async Task State_Passing_Overload_Threads_State()
    {
        var shield = Shield.Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(21, static (state, _) => new ValueTask<int>(state * 2));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task ExecuteOutcome_Captures_Failures_Without_Throwing()
    {
        var shield = Shield.Retry(0, Backoff.None);

        var failure = await shield.ExecuteOutcomeAsync<int>(_ => throw new InvalidOperationException("captured"));
        await Assert.That(failure.IsSuccess).IsFalse();
        await Assert.That(failure.Exception!.Message).IsEqualTo("captured");

        var success = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(5));
        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(success.Result).IsEqualTo(5);
    }

    [Test]
    public async Task Handling_Clauses_Can_Combine_Exception_Types()
    {
        var attempts = 0;
        var shield = Shield.When<InvalidOperationException>().Or<ArgumentException>().Retry(2, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts switch
            {
                1 => throw new InvalidOperationException(),
                2 => throw new ArgumentException(),
                _ => new ValueTask<int>(attempts),
            };
        });

        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Ambient_Handling_Flows_To_Later_Strategies()
    {
        // Retry(1) is outermost; the circuit breaker inside trips on the first InvalidOperationException.
        // The retry then hits the open circuit, and CircuitOpenException is NOT in the handling clause,
        // so it surfaces directly.
        var shield = Shield
            .When<InvalidOperationException>()
            .Retry(1, Backoff.None)
            .CircuitBreaker(1, TimeSpan.FromMinutes(1));

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Sync_Void_Execute_Works()
    {
        var ran = false;
        Shield.Retry(1, Backoff.None).Execute(_ => ran = true);
        await Assert.That(ran).IsTrue();
    }
}

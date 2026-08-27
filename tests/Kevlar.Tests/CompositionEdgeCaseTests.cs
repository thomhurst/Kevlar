namespace Kevlar.Tests;

public class CompositionEdgeCaseTests
{
    [Test]
    public async Task Compose_With_No_Policies_Passes_Through()
    {
        var composed = Shield.Compose();
        var result = await composed.ExecuteAsync(_ => new ValueTask<int>(7));
        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task Compose_With_One_Shield_Preserves_Its_Strategy()
    {
        var calls = 0;
        var composed = Shield.Compose(Shield.Retry(1, Backoff.None));

        var result = await composed.ExecuteAsync(_ =>
        {
            calls++;
            return calls == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(7);
        });

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Compose_Shares_Stateful_Strategies()
    {
        var breaker = Shield.CircuitBreaker(1, TimeSpan.FromMinutes(1));
        var composedA = Shield.Compose(Shield.Retry(0, Backoff.None), breaker);
        var composedB = Shield.Compose(Shield.Timeout(TimeSpan.FromMinutes(1)), breaker);

        await Assert.That(async () => await composedA.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(async () => await composedB.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Retry_Outside_A_Breaker_Retries_Into_The_Open_Circuit()
    {
        var attempts = 0;

        // Classic Polly composition: the breaker trips mid-retry-loop; subsequent retries hit
        // the open circuit instead of the delegate, and the final failure is the rejection.
        var shield = Shield
            .Retry(4, Backoff.None)
            .CircuitBreaker(2, TimeSpan.FromMinutes(1));

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<CircuitOpenException>();

        // Only the first two attempts reached the delegate; the rest were rejected fail-fast.
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task A_New_Handling_Clause_Replaces_The_Ambient_One()
    {
        var calls = new List<string>();
        var shield = Shield
            .When<ArgumentException>()
            .Retry(5, Backoff.None)
            .When<InvalidOperationException>()
            .Retry(1, Backoff.None);

        // Call sequence: ArgumentException (only the outer retry handles it),
        // then InvalidOperationException twice (only the inner retry handles those,
        // and it exhausts after one retry), surfacing the final failure.
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            calls.Add("attempt");
            throw calls.Count == 1 ? new ArgumentException() : new InvalidOperationException("final");
        })).Throws<InvalidOperationException>().WithMessage("final");

        await Assert.That(calls.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Wrapping_A_Typed_Policy_Seals_Its_Result_Handling_For_Later_Strategies()
    {
        var typed = Shield.For<int>().WhenResult(value => value < 0).Timeout(TimeSpan.FromMinutes(1));
        var combined = Shield.Timeout(TimeSpan.FromMinutes(1)).Wrap(typed).FallbackTo(99);

        // Composition sealed the result clause, so the default fallback ignores successful results.
        var result = await combined.ExecuteAsync(_ => new ValueTask<int>(-5));

        await Assert.That(result).IsEqualTo(-5);
    }

    [Test]
    public async Task Lifting_With_For_Carries_The_Ambient_Clause()
    {
        var shield = Shield
            .When<ArgumentException>()
            .Timeout(TimeSpan.FromMinutes(1))
            .For<string>()
            .FallbackTo("recovered");

        // The ArgumentException clause carries across For<T>(): the fallback recovers matching
        // exceptions and lets everything else surface untouched.
        var recovered = await shield.ExecuteAsync(_ => throw new ArgumentException());
        await Assert.That(recovered).IsEqualTo("recovered");

        await Assert.That(async () => await shield.ExecuteAsync<string>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task A_Custom_Strategy_That_Throws_Is_Seen_As_A_Failure_By_Outer_Strategies()
    {
        var strategyCalls = 0;
        var delegateCalls = 0;
        var shield = Shield
            .Retry(2, Backoff.None)
            .Use(new ThrowingStrategy(() => strategyCalls++));

        await Assert.That(async () => await shield.ExecuteAsync(_ =>
        {
            delegateCalls++;
            return new ValueTask<int>(1);
        })).Throws<InvalidOperationException>();

        // The outer retry saw the strategy's exception as a handled failure and retried it;
        // the delegate itself was never reached.
        await Assert.That(strategyCalls).IsEqualTo(3);
        await Assert.That(delegateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task WithName_Returns_A_Copy_And_Leaves_The_Original_Unnamed()
    {
        var original = Shield.Retry(1, Backoff.None);
        var named = original.WithName("named");

        await Assert.That(original.Name).IsNull();
        await Assert.That(named.Name).IsEqualTo("named");
    }

    [Test]
    public async Task WithName_Copies_Share_Stateful_Strategies()
    {
        var original = Shield.CircuitBreaker(1, TimeSpan.FromMinutes(1));
        var named = original.WithName("named");

        await Assert.That(async () => await named.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        // The copy and the original are the same circuit.
        await Assert.That(async () => await original.ExecuteAsync(_ => new ValueTask<int>(1)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Typed_Policies_Support_ExecuteOutcome()
    {
        var shield = Shield.For<int>().WhenResultEquals(-1).Retry(0, Backoff.None);

        var failure = await shield.ExecuteOutcomeAsync(_ => throw new InvalidOperationException("boom"));
        await Assert.That(failure.IsSuccess).IsFalse();
        await Assert.That(failure.Exception!.Message).IsEqualTo("boom");

        var success = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(3));
        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(success.Result).IsEqualTo(3);
    }

    [Test]
    public async Task Void_Executions_Flow_Through_The_Pipeline()
    {
        var attempts = 0;
        var shield = Shield.Retry(2, Backoff.None);

        await shield.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new InvalidOperationException();
            }

            return default(ValueTask);
        });

        await Assert.That(attempts).IsEqualTo(2);

        var stateSeen = 0;
        await shield.ExecuteAsync(41, (state, _) =>
        {
            stateSeen = state + 1;
            return default;
        });

        await Assert.That(stateSeen).IsEqualTo(42);
    }

    [Test]
    public async Task A_Full_Pipeline_Composes_All_Strategy_Types()
    {
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .FallbackTo(-1)
            .Retry(2, Backoff.None)
            .CircuitBreaker(10, TimeSpan.FromMinutes(1))
            .Timeout(TimeSpan.FromMinutes(1))
            .ConcurrencyLimit(4, 4)
            .RateLimit(100, TimeSpan.FromSeconds(1));

        var success = await shield.ExecuteAsync(_ => new ValueTask<int>(42));
        await Assert.That(success).IsEqualTo(42);

        var attempts = 0;
        var recovered = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        // The outermost fallback recovers only after the retries inside it are exhausted.
        await Assert.That(recovered).IsEqualTo(-1);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Nested_Wraps_Preserve_Strategy_Order()
    {
        var log = new List<string>();
        var a = Shield.Use(new MarkerStrategy(log, "a"));
        var b = Shield.Use(new MarkerStrategy(log, "b"));
        var c = Shield.Use(new MarkerStrategy(log, "c"));

        await a.Wrap(b.Wrap(c)).ExecuteAsync(_ =>
        {
            log.Add("action");
            return new ValueTask<int>(1);
        });

        await Assert.That(log).IsEquivalentTo(["a:enter", "b:enter", "c:enter", "action", "c:exit", "b:exit", "a:exit"]);
    }

    private sealed class ThrowingStrategy : Strategy
    {
        private readonly Action _onInvoked;

        public ThrowingStrategy(Action onInvoked) => _onInvoked = onInvoked;

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context)
        {
            _onInvoked();
            throw new InvalidOperationException("strategy blew up");
        }
    }

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
}

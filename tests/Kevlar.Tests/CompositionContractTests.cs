using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CompositionContractTests
{
    [Test]
    [Arguments(WrapKind.UntypedUntyped)]
    [Arguments(WrapKind.UntypedTyped)]
    [Arguments(WrapKind.TypedUntyped)]
    [Arguments(WrapKind.TypedTyped)]
    public async Task Every_Wrap_Form_Preserves_Outer_To_Inner_Order(WrapKind kind)
    {
        var log = new List<string>();
        var outerStrategy = new RecordingStrategy(context => log.Add("outer"));
        var innerStrategy = new RecordingStrategy(context => log.Add("inner"));

        await ExecuteWrappedAsync(
            kind,
            Shield.Use(outerStrategy),
            Shield<int>.Empty.Use(outerStrategy),
            Shield.Use(innerStrategy),
            Shield<int>.Empty.Use(innerStrategy));

        await Assert.That(log).IsEquivalentTo(
            ["outer", "inner"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(WrapKind.UntypedUntyped)]
    [Arguments(WrapKind.UntypedTyped)]
    [Arguments(WrapKind.TypedUntyped)]
    [Arguments(WrapKind.TypedTyped)]
    public async Task Every_Wrap_Form_Uses_First_Available_Metadata(WrapKind kind)
    {
        string? seenName = null;
        TimeProvider? seenTimeProvider = null;
        var innerTimeProvider = new FakeTimeProvider();
        var observer = new RecordingStrategy(context =>
        {
            seenName = context.ShieldName;
            seenTimeProvider = context.TimeProvider;
        });

        await ExecuteWrappedAsync(
            kind,
            Shield.Empty,
            Shield<int>.Empty,
            Shield.Use(observer).WithName("inner").WithTimeProvider(innerTimeProvider),
            Shield<int>.Empty.Use(observer).WithName("inner").WithTimeProvider(innerTimeProvider));

        await Assert.That(seenName).IsEqualTo("inner");
        await Assert.That(ReferenceEquals(seenTimeProvider, innerTimeProvider)).IsTrue();
    }

    [Test]
    [Arguments(WrapKind.UntypedUntyped)]
    [Arguments(WrapKind.UntypedTyped)]
    [Arguments(WrapKind.TypedUntyped)]
    [Arguments(WrapKind.TypedTyped)]
    public async Task Every_Wrap_Form_Prefers_Outer_Metadata(WrapKind kind)
    {
        string? seenName = null;
        TimeProvider? seenTimeProvider = null;
        var outerTimeProvider = new FakeTimeProvider();
        var innerTimeProvider = new FakeTimeProvider();
        var observer = new RecordingStrategy(context =>
        {
            seenName = context.ShieldName;
            seenTimeProvider = context.TimeProvider;
        });

        await ExecuteWrappedAsync(
            kind,
            Shield.Empty.WithName("outer").WithTimeProvider(outerTimeProvider),
            Shield<int>.Empty.WithName("outer").WithTimeProvider(outerTimeProvider),
            Shield.Use(observer).WithName("inner").WithTimeProvider(innerTimeProvider),
            Shield<int>.Empty.Use(observer).WithName("inner").WithTimeProvider(innerTimeProvider));

        await Assert.That(seenName).IsEqualTo("outer");
        await Assert.That(ReferenceEquals(seenTimeProvider, outerTimeProvider)).IsTrue();
    }

    [Test]
    [Arguments(WrapKind.UntypedUntyped)]
    [Arguments(WrapKind.UntypedTyped)]
    [Arguments(WrapKind.TypedUntyped)]
    [Arguments(WrapKind.TypedTyped)]
    public async Task Every_Wrap_Form_Seals_The_Ambient_Clause_For_Appended_Strategies(WrapKind kind)
    {
        var untypedOuter = Shield.When<TimeoutException>().Timeout(TimeSpan.FromMinutes(1));
        var typedOuter = Shield.For<int>().When<TimeoutException>().Timeout(TimeSpan.FromMinutes(1));
        var untypedInner = Shield.When<ArgumentException>().Timeout(TimeSpan.FromMinutes(1));
        var typedInner = Shield.For<int>().When<ArgumentException>().Timeout(TimeSpan.FromMinutes(1));

        if (kind == WrapKind.UntypedUntyped)
        {
            var fallbackCalls = 0;
            var composed = untypedOuter.Wrap(untypedInner);
            var wrapped = composed.FallbackAction((_, _) =>
            {
                fallbackCalls++;
                return default;
            });
            Func<CancellationToken, ValueTask> action = _ => throw new InvalidOperationException();

            await wrapped.ExecuteAsync(action);

            await Assert.That(composed.Ambient).IsNull();
            await Assert.That(fallbackCalls).IsEqualTo(1);
            return;
        }

        var typedWrapped = kind switch
        {
            WrapKind.UntypedTyped => untypedOuter.Wrap(typedInner),
            WrapKind.TypedUntyped => typedOuter.Wrap(untypedInner),
            WrapKind.TypedTyped => typedOuter.Wrap(typedInner),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var result = await typedWrapped.Fallback(42).ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(typedWrapped.Ambient).IsNull();
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Strategies_Inside_Composed_Shields_Keep_Their_Original_Judges()
    {
        var retry = Shield.When<ArgumentException>().Retry(1, Backoff.None);
        var breaker = Shield.When<InvalidOperationException>()
            .CircuitBreaker(1, TimeSpan.FromMinutes(1));
        var composed = Shield.Compose(retry, breaker);
        var attempts = 0;

        await Assert.That(async () => await composed.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw attempts == 1 ? new ArgumentException() : new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(async () => await composed.ExecuteAsync(_ => new ValueTask<int>(42)))
            .Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Non_Composition_Copies_Keep_The_Ambient_Clause()
    {
        var timeProvider = new FakeTimeProvider();
        var untyped = Shield.When<ArgumentException>().Timeout(TimeSpan.FromMinutes(1));
        var typed = Shield.For<int>().When<ArgumentException>().Timeout(TimeSpan.FromMinutes(1));

        await Assert.That(ReferenceEquals(untyped.For<int>().Ambient, untyped.Ambient)).IsTrue();
        await Assert.That(ReferenceEquals(untyped.WithName("named").Ambient, untyped.Ambient)).IsTrue();
        await Assert.That(ReferenceEquals(untyped.WithTimeProvider(timeProvider).Ambient, untyped.Ambient)).IsTrue();
        await Assert.That(ReferenceEquals(typed.WithName("named").Ambient, typed.Ambient)).IsTrue();
        await Assert.That(ReferenceEquals(typed.WithTimeProvider(timeProvider).Ambient, typed.Ambient)).IsTrue();
    }

    [Test]
    public async Task Reusing_A_Builder_Does_Not_Mutate_An_Existing_Shield()
    {
        var builder = Shield.When<InvalidOperationException>();
        var first = builder.Retry(1, Backoff.None);
        var second = builder.Or<ArgumentException>().Retry(1, Backoff.None);
        var firstAttempts = 0;
        var secondAttempts = 0;

        await Assert.That(async () => await first.ExecuteAsync<int>(_ =>
        {
            firstAttempts++;
            throw new ArgumentException();
        })).Throws<ArgumentException>();

        var result = await second.ExecuteAsync(_ =>
        {
            secondAttempts++;
            return secondAttempts == 1
                ? throw new ArgumentException()
                : new ValueTask<int>(42);
        });

        await Assert.That(firstAttempts).IsEqualTo(1);
        await Assert.That(secondAttempts).IsEqualTo(2);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Exception_Predicates_Run_In_Order_And_ShortCircuit()
    {
        var calls = new List<string>();
        var attempts = 0;
        var shield = Shield
            .When(exception =>
            {
                calls.Add("first");
                return exception is ArgumentNullException;
            })
            .Or(exception =>
            {
                calls.Add("second");
                return exception is ArgumentException;
            })
            .Or(_ =>
            {
                calls.Add("third");
                throw new InvalidOperationException("must be short-circuited");
            })
            .Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? throw new ArgumentOutOfRangeException()
                : new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(calls).IsEquivalentTo(
            ["first", "second"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Result_Equality_Handles_Null_And_Default()
    {
        string? nullValue = null;
        var defaultValue = default(int);
        var nullShield = Shield.For<string?>().WhenResult(nullValue).Fallback("null");
        var defaultShield = Shield.For<int>().WhenResult(defaultValue).Fallback(42);

        var nullResult = await nullShield.ExecuteAsync(_ => new ValueTask<string?>((string?)null));
        var defaultResult = await defaultShield.ExecuteAsync(_ => new ValueTask<int>(0));

        await Assert.That(nullResult).IsEqualTo("null");
        await Assert.That(defaultResult).IsEqualTo(42);
    }

    [Test]
    public async Task Predicate_Failure_Surfaces_The_Original_Instance()
    {
        var predicateFailure = new InvalidOperationException("predicate failed");
        var shield = Shield.When(_ => throw predicateFailure).Retry(1, Backoff.None);
        Exception? caught = null;

        try
        {
            await shield.ExecuteAsync<int>(_ => throw new ArgumentException());
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await Assert.That(ReferenceEquals(caught, predicateFailure)).IsTrue();
    }

    [Test]
    [Arguments(StatefulStrategyKind.ConcurrencyLimit)]
    [Arguments(StatefulStrategyKind.RateLimit)]
    [Arguments(StatefulStrategyKind.CircuitBreaker)]
    public async Task Reusing_One_Stateful_Strategy_Instance_In_A_Chain_Is_Rejected(
        StatefulStrategyKind kind)
    {
        var stateful = kind switch
        {
            StatefulStrategyKind.ConcurrencyLimit => Shield.ConcurrencyLimit(1),
            StatefulStrategyKind.RateLimit => Shield.RateLimit(1, TimeSpan.FromSeconds(1)),
            StatefulStrategyKind.CircuitBreaker => Shield.CircuitBreaker(1, TimeSpan.FromSeconds(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        InvalidOperationException? error = null;

        try
        {
            _ = stateful.Wrap(stateful);
        }
        catch (InvalidOperationException caught)
        {
            error = caught;
        }

        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("same strategy instance");
        await Assert.That(error.Message).Contains("deadlock or double-count");
    }

    [Test]
    public async Task Independently_Created_Equivalent_Strategies_Are_Allowed()
    {
        var shield = Shield.ConcurrencyLimit(1).Wrap(Shield.ConcurrencyLimit(1));

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Reusing_A_Stateless_Strategy_Instance_In_A_Chain_Is_Allowed()
    {
        var calls = 0;
        var strategy = new RecordingStrategy(_ => calls++);
        var reusable = Shield.Use(strategy);
        var shield = reusable.Wrap(reusable);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task Custom_Stateful_Strategy_Can_Reject_Duplicate_References()
    {
        var strategy = new StatefulCustomStrategy();
        var stateful = Shield.Use(strategy);

        await Assert.That(() => stateful.Wrap(stateful)).Throws<InvalidOperationException>();
    }

    private static ValueTask<int> ExecuteWrappedAsync(
        WrapKind kind,
        Shield untypedOuter,
        Shield<int> typedOuter,
        Shield untypedInner,
        Shield<int> typedInner) =>
        kind switch
        {
            WrapKind.UntypedUntyped => untypedOuter.Wrap(untypedInner).ExecuteAsync(_ => new ValueTask<int>(42)),
            WrapKind.UntypedTyped => untypedOuter.Wrap(typedInner).ExecuteAsync(_ => new ValueTask<int>(42)),
            WrapKind.TypedUntyped => typedOuter.Wrap(untypedInner).ExecuteAsync(_ => new ValueTask<int>(42)),
            WrapKind.TypedTyped => typedOuter.Wrap(typedInner).ExecuteAsync(_ => new ValueTask<int>(42)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public enum WrapKind
    {
        UntypedUntyped,
        UntypedTyped,
        TypedUntyped,
        TypedTyped,
    }

    public enum StatefulStrategyKind
    {
        ConcurrencyLimit,
        RateLimit,
        CircuitBreaker,
    }

    private sealed class RecordingStrategy : Strategy
    {
        private readonly Action<KevlarContext> _record;

        public RecordingStrategy(Action<KevlarContext> record) => _record = record;

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            _record(context);
            return next.InvokeAsync(context);
        }
    }

    private sealed class StatefulCustomStrategy : Strategy
    {
        protected internal override bool IsDuplicateReferenceUnsafe => true;

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }
}

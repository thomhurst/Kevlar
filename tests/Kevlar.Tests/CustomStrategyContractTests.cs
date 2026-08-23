using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class CustomStrategyContractTests
{
    private static readonly KevlarKey<string> PropertyKey = new("custom-strategy");

    [Test]
    public async Task Zero_Invocations_Can_Short_Circuit_With_Result_Or_Failure()
    {
        var actionCalls = 0;
        var result = await Shield.Use(new ResultStrategy(42)).ExecuteAsync<int>(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(0);
        });

        var failure = new InvalidOperationException("short-circuited");
        var outcome = await Shield.Use(new FailureStrategy(failure)).ExecuteOutcomeAsync<int>(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(0);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(ReferenceEquals(outcome.Exception, failure)).IsTrue();
        await Assert.That(actionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Multiple_Sequential_Invocations_Run_The_Remainder_Each_Time()
    {
        var attempts = 0;
        var result = await Shield.Use(new RepeatStrategy(3)).ExecuteAsync(_ =>
            new ValueTask<int>(++attempts));

        await Assert.That(result).IsEqualTo(3);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task One_Invocation_Runs_The_Remainder_Exactly_Once()
    {
        var actionCalls = 0;
        var result = await Shield.Use(PassThroughStrategy.Instance).ExecuteAsync(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(actionCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Synchronous_Strategy_Throw_Becomes_Outcome_For_Outer_Fallback()
    {
        var failure = new InvalidOperationException("sync strategy failure");
        Exception? observed = null;
        var actionCalls = 0;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Fallback(42, options => options.OnFallback = fallback => observed = fallback.Outcome.Exception)
            .Use(new ThrowingStrategy(failure));

        var result = await shield.ExecuteAsync(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(0);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(ReferenceEquals(observed, failure)).IsTrue();
        await Assert.That(actionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Synchronous_Strategy_Throw_Becomes_Outcome_For_Outer_Retry()
    {
        var failure = new InvalidOperationException("retry strategy failure");
        var strategy = new ThrowingStrategy(failure);
        var actionCalls = 0;
        var shield = Shield.When<InvalidOperationException>()
            .Retry(2, Backoff.None)
            .Use(strategy);

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(0);
        });

        await Assert.That(ReferenceEquals(outcome.Exception, failure)).IsTrue();
        await Assert.That(strategy.Invocations).IsEqualTo(3);
        await Assert.That(actionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Asynchronous_Strategy_Fault_Becomes_Outcome_For_Outer_Fallback()
    {
        var failure = new InvalidOperationException("async strategy failure");
        Exception? observed = null;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Fallback(42, options => options.OnFallback = fallback => observed = fallback.Outcome.Exception)
            .Use(new AsynchronouslyFaultingStrategy(failure));

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(0));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(ReferenceEquals(observed, failure)).IsTrue();
    }

    [Test]
    public async Task Returned_Failure_Outcome_Matches_Thrown_Strategy_Failure()
    {
        var failure = new InvalidOperationException("returned failure");
        Exception? observed = null;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Fallback(42, options => options.OnFallback = fallback => observed = fallback.Outcome.Exception)
            .Use(new FailureStrategy(failure));

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(0));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(ReferenceEquals(observed, failure)).IsTrue();
    }

    [Test]
    public async Task Untyped_Async_Execution_Preserves_Context_Properties_And_State()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var state = new ExecutionState(42);
        var observer = new ContextObserverStrategy();
        var propertyObserver = new PropertyObserverStrategy();
        var shield = Shield.Use(observer)
            .Use(propertyObserver)
            .WithName("custom")
            .WithTimeProvider(timeProvider);

        var result = await shield.ExecuteAsync(state, async (executionState, token) =>
        {
            await Task.Yield();
            executionState.IsSameInstance = ReferenceEquals(executionState, state);
            executionState.SeenToken = token;
            return executionState.Value;
        }, cancellation.Token);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(state.IsSameInstance).IsTrue();
        await Assert.That(propertyObserver.SeenProperty).IsEqualTo("set-by-strategy");
        await Assert.That(state.SeenToken).IsEqualTo(cancellation.Token);
        await Assert.That(observer.Snapshot)
            .IsEqualTo(new ContextSnapshot("custom", timeProvider, cancellation.Token, false));
    }

    [Test]
    public async Task Typed_Sync_Execution_Preserves_Context_Properties_And_State()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var state = new ExecutionState(42);
        var observer = new ContextObserverStrategy();
        var propertyObserver = new PropertyObserverStrategy();
        var shield = Shield<int>.Empty
            .Use(observer)
            .Use(propertyObserver)
            .WithName("typed-custom")
            .WithTimeProvider(timeProvider);

        var result = shield.Execute(state, (executionState, token) =>
        {
            executionState.IsSameInstance = ReferenceEquals(executionState, state);
            executionState.SeenToken = token;
            return executionState.Value;
        }, cancellation.Token);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(state.IsSameInstance).IsTrue();
        await Assert.That(propertyObserver.SeenProperty).IsEqualTo("set-by-strategy");
        await Assert.That(state.SeenToken).IsEqualTo(cancellation.Token);
        await Assert.That(observer.Snapshot)
            .IsEqualTo(new ContextSnapshot("typed-custom", timeProvider, cancellation.Token, true));
    }

    [Test]
    public async Task Stateless_Strategy_Is_Safe_For_Parallel_Reuse()
    {
        var strategy = new CountingPassThroughStrategy();
        var shield = Shield.Use(strategy);
        var executions = Enumerable.Range(0, 128)
            .Select(value => shield.ExecuteAsync(value, ExecuteAsynchronously).AsTask());

        var results = await Task.WhenAll(executions);

        await Assert.That(results).IsEquivalentTo(Enumerable.Range(0, 128));
        await Assert.That(strategy.Invocations).IsEqualTo(128);
    }

    [Test]
    public async Task Describe_Uses_Override_Or_Concrete_Type_Name()
    {
        await Assert.That(Shield.Use(PassThroughStrategy.Instance).ToString())
            .IsEqualTo(nameof(PassThroughStrategy));
        await Assert.That(Shield.Use(new DescribedStrategy()).ToString())
            .IsEqualTo("CustomDescription");
    }

    [Test]
    public async Task Use_Rejects_Null_Strategies()
    {
        await Assert.That(() => Shield.Use(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield<int>.Empty.Use(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Default_Continuation_Returns_Clear_Failure_Outcome()
    {
        var outcome = await Shield.Use(new InvalidContinuationStrategy())
            .ExecuteOutcomeAsync<int>(_ => new ValueTask<int>(42));

        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(outcome.Exception!.Message).IsEqualTo("The continuation is not initialized.");
    }

    private static async ValueTask<int> ExecuteAsynchronously(int value, CancellationToken _)
    {
        await Task.Yield();
        return value;
    }

    private sealed class ExecutionState(int value)
    {
        public int Value { get; } = value;

        public bool IsSameInstance { get; set; }

        public CancellationToken SeenToken { get; set; }
    }

    private sealed class ContextObserverStrategy : Strategy
    {
        public ContextSnapshot? Snapshot { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Snapshot = new ContextSnapshot(
                context.ShieldName,
                context.TimeProvider,
                context.CancellationToken,
                context.IsSynchronous);
            context.Properties.Set(PropertyKey, "set-by-strategy");
            return next.InvokeAsync(context);
        }
    }

    private sealed class PropertyObserverStrategy : Strategy
    {
        public string? SeenProperty { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            SeenProperty = context.Properties.GetOrDefault<string>(PropertyKey);
            return next.InvokeAsync(context);
        }
    }

    private sealed class ResultStrategy(int result) : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            new(Outcome<T>.FromResult((T)(object)result));
    }

    private sealed class FailureStrategy(Exception failure) : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            new(Outcome<T>.FromException(failure));
    }

    private sealed class RepeatStrategy(int count) : Strategy
    {
        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var outcome = default(Outcome<T>);
            for (var invocation = 0; invocation < count; invocation++)
            {
                outcome = await next.InvokeAsync(context);
            }

            return outcome;
        }
    }

    private sealed class ThrowingStrategy(Exception failure) : Strategy
    {
        private int _invocations;

        public int Invocations => Volatile.Read(ref _invocations);

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Interlocked.Increment(ref _invocations);
            throw failure;
        }
    }

    private sealed class AsynchronouslyFaultingStrategy(Exception failure) : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            FaultAsync<T>();

        private async ValueTask<Outcome<TResult>> FaultAsync<TResult>()
        {
            await Task.Yield();
            throw failure;
        }
    }

    private sealed class PassThroughStrategy : Strategy
    {
        public static PassThroughStrategy Instance { get; } = new();

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            next.InvokeAsync(context);
    }

    private sealed class CountingPassThroughStrategy : Strategy
    {
        private int _invocations;

        public int Invocations => Volatile.Read(ref _invocations);

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Interlocked.Increment(ref _invocations);
            return next.InvokeAsync(context);
        }
    }

    private sealed class DescribedStrategy : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            next.InvokeAsync(context);

        public override string Describe() => "CustomDescription";
    }

    private sealed class InvalidContinuationStrategy : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) =>
            default(Continuation<T, TState>).InvokeAsync(context);
    }

    private sealed record ContextSnapshot(
        string? Name,
        TimeProvider TimeProvider,
        CancellationToken CancellationToken,
        bool IsSynchronous);
}

using System.Collections.Concurrent;
using Kevlar.Strategies;

namespace Kevlar.Tests;

[NotInParallel]
public class CallerContextTests
{
    private static readonly KevlarKey<string> RequestId = new("request-id");
    private static readonly KevlarKey<int> AttemptValue = new("attempt-value");

    [Test]
    public async Task Caller_Properties_Are_Visible_Before_The_Outermost_Strategy()
    {
        var observer = new PropertyObserverStrategy(RequestId);
        var shield = Shield.Use(observer);

        var result = await shield.ExecuteWithContextAsync(
            "request-42",
            static (state, properties) => properties.Set(RequestId, state),
            static (_, context) => new ValueTask<string>(context.Properties.GetOrDefault<string>(RequestId)!));

        await Assert.That(result).IsEqualTo("request-42");
        await Assert.That(observer.ObservedValue).IsEqualTo("request-42");
    }

    [Test]
    public async Task Retry_Reuses_The_Logical_Context_And_Effective_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var contexts = new List<(KevlarContext Context, CancellationToken Token)>();
        var attempts = 0;
        var callbackValue = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = retry =>
                callbackValue = retry.Context.Properties.GetOrDefault(AttemptValue);
        });

        var result = await shield.ExecuteWithContextAsync(
            contexts,
            static (_, properties) => properties.Set(AttemptValue, 0),
            (state, context) =>
            {
                state.Add((context, context.CancellationToken));
                context.Properties.Set(AttemptValue, ++attempts);
                if (attempts == 1)
                {
                    throw new InvalidOperationException("retry");
                }

                return new ValueTask<int>(context.Properties.GetOrDefault(AttemptValue));
            },
            cancellation.Token);

        await Assert.That(result).IsEqualTo(2);
        await Assert.That(contexts).Count().IsEqualTo(2);
        await Assert.That(ReferenceEquals(contexts[0].Context, contexts[1].Context)).IsTrue();
        await Assert.That(contexts[1].Token).IsEqualTo(cancellation.Token);
        await Assert.That(callbackValue).IsEqualTo(1);
    }

    [Test]
    public async Task Hedge_Forks_Seeded_Properties_And_Isolates_Attempt_Mutations()
    {
        var observations = new ConcurrentBag<(KevlarContext Context, int Initial, int Updated)>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = 0;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .Hedge(2, TimeSpan.Zero);

        var result = await shield.ExecuteWithContextAsync(
            observations,
            static (_, properties) => properties.Set(AttemptValue, 7),
            async (state, context) =>
            {
                var currentAttempt = Interlocked.Increment(ref attempt);
                var initial = context.Properties.GetOrDefault(AttemptValue);
                context.Properties.Set(AttemptValue, currentAttempt);
                state.Add((context, initial, context.Properties.GetOrDefault(AttemptValue)));

                if (currentAttempt == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    throw new InvalidOperationException("primary");
                }

                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                release.TrySetResult();
                return 42;
            });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observations).Count().IsEqualTo(2);
        await Assert.That(observations.All(static observation => observation.Initial == 7)).IsTrue();
        await Assert.That(observations.Select(static observation => observation.Context).Distinct()).Count().IsEqualTo(2);
        await Assert.That(observations.Select(static observation => observation.Updated).Order()).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task Context_Action_Observes_Timeout_Cancellation_Token()
    {
        var shield = Shield.Timeout(TimeSpan.FromMinutes(1));
        CancellationToken observed = default;

        _ = await shield.ExecuteWithContextAsync(
            42,
            static (_, _) => { },
            (state, context) =>
            {
                observed = context.CancellationToken;
                return new ValueTask<int>(state);
            });

        await Assert.That(observed.CanBeCanceled).IsTrue();
    }

    [Test]
    public async Task PreCancellation_Skips_Initializer_And_Action()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var initialized = false;
        var invoked = false;

        var exception = await Assert.That(async () => await Shield.Empty.ExecuteWithContextAsync(
                0,
                (_, _) => initialized = true,
                (_, _) =>
                {
                    invoked = true;
                    return new ValueTask<int>(42);
                },
                cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(initialized).IsFalse();
        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task Initializer_Failure_Returns_A_Clean_Context_To_The_Pool()
    {
        await Assert.That(async () => await Shield.Empty.ExecuteWithContextAsync(
                0,
                static (_, properties) =>
                {
                    properties.Set(RequestId, "dirty");
                    throw new InvalidOperationException("initialize");
                },
                static (_, _) => new ValueTask<int>(42)))
            .Throws<InvalidOperationException>();

        var observer = new PropertyObserverStrategy(RequestId);
        _ = await Shield.Use(observer).ExecuteAsync(static _ => new ValueTask<int>(42));

        await Assert.That(observer.ObservedValue).IsNull();
    }

    [Test]
    public async Task Action_Failure_Clears_The_Exact_Context_Before_Returning_It()
    {
        KevlarContext? captured = null;

        await Assert.That(async () => await Shield.Empty.ExecuteWithContextAsync(
                0,
                static (_, properties) => properties.Set(RequestId, "dirty"),
                (_, context) =>
                {
                    captured = context;
                    throw new InvalidOperationException("action");
                }))
            .Throws<InvalidOperationException>();

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Properties.TryGet(RequestId, out _)).IsFalse();
        await Assert.That(captured.CancellationToken).IsEqualTo(default(CancellationToken));
        await Assert.That(captured.ShieldName).IsNull();
    }

    [Test]
    public async Task Success_Clears_Caller_Seeded_Properties_Before_Pooling()
    {
        KevlarContext? captured = null;

        _ = await Shield.Empty.ExecuteWithContextAsync(
            42,
            static (_, properties) => properties.Set(RequestId, "dirty"),
            (state, context) =>
            {
                captured = context;
                return new ValueTask<int>(state);
            });

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Properties.TryGet(RequestId, out _)).IsFalse();
    }

    [Test]
    public async Task Initializer_Has_An_Exact_Null_Guard()
    {
        var exception = await Assert.That(async () => await Shield.Empty.ExecuteWithContextAsync<int, int>(
                0,
                null!,
                static (_, _) => new ValueTask<int>(42)))
            .Throws<ArgumentNullException>();

        await Assert.That(exception!.ParamName).IsEqualTo("initializeProperties");
    }

    [Test]
    public async Task Sync_Result_And_Void_Overloads_Expose_Context()
    {
        var result = Shield.Empty.ExecuteWithContext(
            21,
            static (state, properties) => properties.Set(AttemptValue, state),
            static (_, context) => context.Properties.GetOrDefault(AttemptValue) * 2);
        var observed = 0;

        Shield.Empty.ExecuteWithContext(
            7,
            static (state, properties) => properties.Set(AttemptValue, state),
            (_, context) => observed = context.Properties.GetOrDefault(AttemptValue));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observed).IsEqualTo(7);
    }

    [Test]
    public async Task Async_Void_Overload_Exposes_Context()
    {
        var observed = 0;

        await Shield.Empty.ExecuteWithContextAsync(
            9,
            static (state, properties) => properties.Set(AttemptValue, state),
            (_, context) =>
            {
                observed = context.Properties.GetOrDefault(AttemptValue);
                return ValueTask.CompletedTask;
            });

        await Assert.That(observed).IsEqualTo(9);
    }

    [Test]
    public async Task Typed_Shield_Supports_Async_And_Sync_Context_Execution()
    {
        var shield = Shield<int>.Empty;

        var asyncResult = await shield.ExecuteWithContextAsync(
            40,
            static (state, properties) => properties.Set(AttemptValue, state),
            static (_, context) => new ValueTask<int>(context.Properties.GetOrDefault(AttemptValue) + 2));
        var syncResult = shield.ExecuteWithContext(
            40,
            static (state, properties) => properties.Set(AttemptValue, state),
            static (_, context) => context.Properties.GetOrDefault(AttemptValue) + 2);

        await Assert.That(asyncResult).IsEqualTo(42);
        await Assert.That(syncResult).IsEqualTo(42);
    }

    private sealed class PropertyObserverStrategy(KevlarKey<string> key) : Strategy
    {
        public string? ObservedValue { get; private set; }

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            ObservedValue = context.Properties.GetOrDefault<string>(key);
            return next.InvokeAsync(context);
        }
    }
}

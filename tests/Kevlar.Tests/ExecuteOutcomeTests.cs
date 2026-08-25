namespace Kevlar.Tests;

public class ExecuteOutcomeTests
{
    private static readonly KevlarKey<int> AttemptCount = new("attempt-count");
    private static readonly KevlarKey<int> WinningAttempt = new("winning-attempt");
    private static readonly KevlarKey<int> OuterStrategyWrite = new("outer-strategy-write");
    private static readonly KevlarKey<int> RemovedByOuterStrategy = new("removed-by-outer-strategy");
    private static readonly KevlarKey<int> HedgeCallbackWrite = new("hedge-callback-write");
    private static readonly KevlarKey<int> PredicateWrite = new("predicate-write");

    [Test]
    public async Task Void_ExecuteOutcomeAsync_Returns_Success()
    {
        var invoked = false;

        var outcome = await Shield.Empty.ExecuteOutcomeAsync(_ =>
        {
            invoked = true;
            return ValueTask.CompletedTask;
        });

        await Assert.That(invoked).IsTrue();
        await Assert.That(outcome.IsSuccess).IsTrue();
        await Assert.That(outcome.Exception).IsNull();
    }

    [Test]
    public async Task Void_ExecuteOutcomeAsync_Returns_Last_Retry_Exception_Without_Throwing()
    {
        var attempts = 0;
        var outcome = await Shield.Retry(1, Backoff.None).ExecuteOutcomeAsync(_ =>
        {
            attempts++;
            ThrowOriginal();
            return ValueTask.CompletedTask;
        });

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(outcome.IsSuccess).IsFalse();
        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();

        var rethrown = await Assert.That(outcome.Rethrow).Throws<InvalidOperationException>();
        await Assert.That(rethrown!.StackTrace).Contains(nameof(ThrowOriginal));
    }

    [Test]
    public async Task Void_ExecuteOutcomeAsync_Preserves_Caller_Cancellation_As_Outcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await Shield.Empty.ExecuteOutcomeAsync(
            static _ => throw new InvalidOperationException("must not run"),
            cancellation.Token);

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Void_ExecuteOutcomeAsync_Reports_Rejection_Exception()
    {
        var shield = Shield.ConcurrencyLimit(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = shield.ExecuteAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;

        var outcome = await shield.ExecuteOutcomeAsync(static _ => ValueTask.CompletedTask);
        release.SetResult();
        await active;

        await Assert.That(outcome.Exception).IsTypeOf<ConcurrencyLimitExceededException>();
    }

    [Test]
    public async Task Void_ExecuteOutcomeAsync_Captures_Strategy_Rejections()
    {
        var breaker = Shield.CircuitBreaker(
            consecutiveFailures: 1,
            breakDuration: TimeSpan.FromMinutes(1));
        _ = await breaker.ExecuteOutcomeAsync(
            static _ => throw new InvalidOperationException("open breaker"));
        var circuitOutcome = await breaker.ExecuteOutcomeAsync(
            static _ => ValueTask.CompletedTask);

        var limiter = Shield.RateLimit(1, TimeSpan.FromMinutes(1));
        _ = await limiter.ExecuteOutcomeAsync(static _ => ValueTask.CompletedTask);
        var rateLimitOutcome = await limiter.ExecuteOutcomeAsync(
            static _ => ValueTask.CompletedTask);

        var timeoutOutcome = await Shield.Timeout(TimeSpan.FromMilliseconds(10))
            .ExecuteOutcomeAsync(static token => new ValueTask(
                Task.Delay(Timeout.InfiniteTimeSpan, token)));

        await Assert.That(circuitOutcome.Exception).IsTypeOf<CircuitOpenException>();
        await Assert.That(((CircuitOpenException)circuitOutcome.Exception!).RetryAfter).IsNotNull();
        await Assert.That(rateLimitOutcome.Exception).IsTypeOf<RateLimitExceededException>();
        await Assert.That(((RateLimitExceededException)rateLimitOutcome.Exception!).RetryAfter).IsNotNull();
        await Assert.That(timeoutOutcome.Exception).IsTypeOf<TimeoutExceededException>();
    }

    [Test]
    public async Task Void_ExecuteOutcomeAsync_Runs_Fallback_Then_Reports_Success()
    {
        var fallbackRan = false;
        var shield = Shield
            .When<InvalidOperationException>()
            .Fallback(_ =>
            {
                fallbackRan = true;
                return ValueTask.CompletedTask;
            });

        var outcome = await shield.ExecuteOutcomeAsync(
            static _ => throw new InvalidOperationException("boom"));

        await Assert.That(fallbackRan).IsTrue();
        await Assert.That(outcome.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Task_And_State_Overloads_Return_Void_Outcomes()
    {
        var success = await Shield.Empty.ExecuteOutcomeAsync(
            42,
            static (state, _) => state == 42 ? Task.CompletedTask : Task.FromException(new Exception()));
        var failure = await Shield.Empty.ExecuteOutcomeAsync(
            "boom",
            static (state, _) => Task.FromException(new InvalidOperationException(state)));

        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(failure.Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure.Exception!.Message).IsEqualTo("boom");
    }

    [Test]
    public async Task Sync_ExecuteOutcome_Twins_Return_Void_And_Typed_Outcomes()
    {
        var voidSuccess = Shield.Empty.ExecuteOutcome(static _ => { });
        var voidFailure = Shield.Empty.ExecuteOutcome(
            "sync",
            static (state, _) => throw new InvalidOperationException(state));
        var result = Shield.Empty.ExecuteOutcome(static _ => 42);
        var typed = Shield<int>.Empty.ExecuteOutcome(
            42,
            static (state, _) => state);

        await Assert.That(voidSuccess.IsSuccess).IsTrue();
        await Assert.That(voidFailure.Exception!.Message).IsEqualTo("sync");
        await Assert.That(result.Result).IsEqualTo(42);
        await Assert.That(typed.Result).IsEqualTo(42);
    }

    [Test]
    public async Task Generic_Outcome_Implicitly_Converts_To_Void_Outcome()
    {
        Outcome success = Outcome<int>.FromResult(42);
        var exception = new InvalidOperationException("boom");
        Outcome failure = Outcome<int>.FromException(exception);

        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(failure.Exception).IsSameReferenceAs(exception);
    }

    [Test]
    public async Task OnCompleted_Receives_Properties_Before_Context_Returns_To_Pool()
    {
        var observed = -1;
        var callbackCouldRead = false;
        var shield = Shield.Use(new PropertyWritingStrategy());

        var result = await shield.ExecuteWithContextAsync(
            42,
            static (_, _) => { },
            static (state, _) => new ValueTask<int>(state),
            (_, properties) =>
            {
                callbackCouldRead = properties.Count > 0;
                observed = properties.GetOrDefault(AttemptCount, -1);
            });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(callbackCouldRead).IsTrue();
        await Assert.That(observed).IsEqualTo(1);
    }

    [Test]
    public async Task OnCompleted_Runs_On_Exception_Path_And_Does_Not_Mask_Outcome()
    {
        var observed = -1;
        var original = new InvalidOperationException("original");
        var shield = Shield.Use(new PropertyWritingStrategy());

        var thrown = await Assert.That(async () => await shield.ExecuteWithContextAsync<int, int>(
                0,
                static (_, _) => { },
                (_, _) => throw original,
                (_, properties) => observed = properties.GetOrDefault(AttemptCount, -1)))
            .Throws<InvalidOperationException>();

        var result = await shield.ExecuteWithContextAsync(
            42,
            static (_, _) => { },
            static (state, _) => new ValueTask<int>(state),
            static (_, _) => throw new Exception("observer"));

        await Assert.That(thrown).IsSameReferenceAs(original);
        await Assert.That(observed).IsEqualTo(1);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task OnCompleted_Runs_For_PreCancelled_Execution_With_Empty_Properties()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var initialized = false;
        var invoked = false;
        var completed = false;
        var propertyCount = -1;

        await Assert.That(async () => await Shield.Empty.ExecuteWithContextAsync(
                0,
                (_, _) => initialized = true,
                (_, _) =>
                {
                    invoked = true;
                    return new ValueTask<int>(42);
                },
                (_, properties) =>
                {
                    completed = true;
                    propertyCount = properties.Count;
                },
                cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(initialized).IsFalse();
        await Assert.That(invoked).IsFalse();
        await Assert.That(completed).IsTrue();
        await Assert.That(propertyCount).IsEqualTo(0);
    }

    [Test]
    public async Task OnCompleted_Does_Not_Observe_A_Previous_Execution()
    {
        var first = -1;
        var second = -1;

        await Shield.Empty.ExecuteWithContextAsync(
            1,
            static (state, properties) => properties.Set(AttemptCount, state),
            static (_, _) => ValueTask.CompletedTask,
            (_, properties) => first = properties.GetOrDefault(AttemptCount, -1));
        await Shield.Empty.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            static (_, _) => ValueTask.CompletedTask,
            (_, properties) => second = properties.GetOrDefault(AttemptCount, -1));

        await Assert.That(first).IsEqualTo(1);
        await Assert.That(second).IsEqualTo(-1);
    }

    [Test]
    public async Task OnCompleted_Sees_Winning_Hedge_Properties()
    {
        var attempts = 0;
        var observed = -1;
        var releasePrimary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await Shield.Hedge(2, TimeSpan.Zero).ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            async (_, context) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                context.Properties.Set(WinningAttempt, attempt);
                if (attempt == 1)
                {
                    await releasePrimary.Task;
                }

                return 42;
            },
            (_, properties) => observed = properties.GetOrDefault(WinningAttempt, -1));
        releasePrimary.SetResult();

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(observed).IsEqualTo(2);
    }

    [Test]
    public async Task OnCompleted_Preserves_Parent_Writes_After_Winning_Attempt_Forked()
    {
        var releasePrimary = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var observed = -1;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = hedge =>
            {
                hedge.Context.Properties.Set(HedgeCallbackWrite, 42);
                releasePrimary.TrySetResult();
            };
        });

        _ = await shield.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            async (_, context) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    await releasePrimary.Task;
                    return 42;
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                return 0;
            },
            (_, properties) =>
                observed = properties.GetOrDefault(HedgeCallbackWrite, -1));

        await Assert.That(observed).IsEqualTo(42);
    }

    [Test]
    public async Task OnCompleted_Sees_Properties_Removed_By_Winning_Hedge()
    {
        var key = new KevlarKey<int>("removed-by-winner");
        var containsKey = true;
        var attempts = 0;

        _ = await Shield.Hedge(2, TimeSpan.Zero).ExecuteWithContextAsync(
            key,
            static (state, properties) => properties.Set(state, 42),
            async (state, context) =>
            {
                if (Interlocked.Increment(ref attempts) == 2)
                {
                    context.Properties.Remove(state);
                    return 42;
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                return 0;
            },
            (_, properties) => containsKey = properties.Contains(key));

        await Assert.That(containsKey).IsFalse();
    }

    [Test]
    public async Task OnCompleted_Sees_Winning_Properties_Through_Nested_Hedges()
    {
        var attempts = 0;
        var observed = -1;
        var shield = Shield
            .Hedge(2, Timeout.InfiniteTimeSpan)
            .Hedge(2, TimeSpan.Zero);

        _ = await shield.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            async (_, context) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                context.Properties.Set(WinningAttempt, attempt);
                if (attempt == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                }

                return 42;
            },
            (_, properties) => observed = properties.GetOrDefault(WinningAttempt, -1));

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(observed).IsEqualTo(2);
    }

    [Test]
    public async Task OnCompleted_Sees_Properties_From_Generated_Original_Action()
    {
        var attempts = 0;
        var observedWinner = -1;
        var retainedRemovedKey = true;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge => token => hedge.OriginalAction(token);
        });

        _ = await shield.ExecuteWithContextAsync(
            0,
            static (_, properties) => properties.Set(RemovedByOuterStrategy, 1),
            (_, context) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                context.Properties.Set(WinningAttempt, attempt);
                context.Properties.Remove(RemovedByOuterStrategy);
                return attempt == 1
                    ? ValueTask.FromException<int>(new InvalidOperationException())
                    : new ValueTask<int>(42);
            },
            (_, properties) =>
            {
                observedWinner = properties.GetOrDefault(WinningAttempt, -1);
                retainedRemovedKey = properties.Contains(RemovedByOuterStrategy);
            });

        await Assert.That(observedWinner).IsEqualTo(2);
        await Assert.That(retainedRemovedKey).IsFalse();
    }

    [Test]
    public async Task OnCompleted_Uses_Later_Original_Action_When_Results_Are_Equal()
    {
        var attempts = 0;
        var observedWinner = -1;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge => async token =>
            {
                _ = await hedge.OriginalAction(token);
                return await hedge.OriginalAction(token);
            };
        });

        _ = await shield.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            (_, context) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                context.Properties.Set(WinningAttempt, attempt);
                return attempt == 1
                    ? ValueTask.FromException<int>(new InvalidOperationException())
                    : new ValueTask<int>(42);
            },
            (_, properties) =>
                observedWinner = properties.GetOrDefault(WinningAttempt, -1));

        await Assert.That(observedWinner).IsEqualTo(3);
    }

    [Test]
    public async Task OnCompleted_Preserves_Generated_Winner_Predicate_Writes()
    {
        var attempts = 0;
        var observed = -1;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.HandlesException = static _ => true;
            options.HandlesResultWithContext = handling =>
            {
                handling.Context.Properties.Set(PredicateWrite, 42);
                return false;
            };
            options.ActionGenerator = hedge => token => hedge.OriginalAction(token);
        });

        _ = await shield.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            (_, _) => Interlocked.Increment(ref attempts) == 1
                ? ValueTask.FromException<int>(new InvalidOperationException())
                : new ValueTask<int>(42),
            (_, properties) =>
                observed = properties.GetOrDefault(PredicateWrite, -1));

        await Assert.That(observed).IsEqualTo(42);
    }

    [Test]
    public async Task OnCompleted_Sees_Post_Hedge_Strategy_Writes_And_Removals()
    {
        var attempts = 0;
        var observedWinner = -1;
        var observedOuterWrite = -1;
        var retainedRemovedKey = true;
        var shield = Shield.Empty
            .Use(new PostHedgePropertyStrategy())
            .Hedge(2, TimeSpan.Zero);

        _ = await shield.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            async (_, context) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                context.Properties.Set(WinningAttempt, attempt);
                context.Properties.Set(RemovedByOuterStrategy, 1);
                if (attempt == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                }

                return 42;
            },
            (_, properties) =>
            {
                observedWinner = properties.GetOrDefault(WinningAttempt, -1);
                observedOuterWrite = properties.GetOrDefault(OuterStrategyWrite, -1);
                retainedRemovedKey = properties.Contains(RemovedByOuterStrategy);
            });

        await Assert.That(observedWinner).IsEqualTo(2);
        await Assert.That(observedOuterWrite).IsEqualTo(42);
        await Assert.That(retainedRemovedKey).IsFalse();
    }

    private static void ThrowOriginal() => throw new InvalidOperationException("boom");

    private sealed class PropertyWritingStrategy : Strategy
    {
        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            context.Properties.Set(AttemptCount, 1);
            return await next.InvokeAsync(context);
        }
    }

    private sealed class PostHedgePropertyStrategy : Strategy
    {
        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var outcome = await next.InvokeAsync(context);
            context.Properties.Set(OuterStrategyWrite, 42);
            context.Properties.Remove(RemovedByOuterStrategy);
            return outcome;
        }
    }
}

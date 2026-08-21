namespace Kevlar.Tests;

public class HedgingActionGeneratorTests
{
    [Test]
    public async Task Typed_Generator_Selects_A_Distinct_Action()
    {
        var primaryCalls = 0;
        var generatedAttempts = new List<int>();
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxAttempts = 3;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(hedge =>
            {
                generatedAttempts.Add(hedge.Attempt);
                return _ => new ValueTask<int>(hedge.Attempt * 10);
            });
        });

        var result = await shield.ExecuteAsync<int>(_ =>
        {
            primaryCalls++;
            throw new InvalidOperationException("primary");
        });

        await Assert.That(result).IsEqualTo(20);
        await Assert.That(primaryCalls).IsEqualTo(1);
        await Assert.That(generatedAttempts).IsEquivalentTo([2]);
    }

    [Test]
    public async Task Null_Generated_Action_Runs_The_Original_Inner_Pipeline()
    {
        var key = new KevlarKey<int>("hedge-target");
        var observer = new PropertyObserver(key);
        var attempts = 0;
        var shield = Shield.Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = HedgeActionGenerator.Create<int>(hedge =>
                {
                    hedge.Context.Properties.Set(key, hedge.Attempt);
                    return null;
                });
            })
            .Use(observer);

        var result = await shield.ExecuteAsync(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            return attempt == 1
                ? ValueTask.FromException<int>(new InvalidOperationException("primary"))
                : new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observer.Values).IsEquivalentTo([-1, 2]);
    }

    [Test]
    public async Task Untyped_Void_Generator_Selects_A_Distinct_Action()
    {
        var originalCalls = 0;
        var generatedCalls = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create(hedge =>
            {
                return _ =>
                {
                    generatedCalls += hedge.Attempt;
                    return ValueTask.CompletedTask;
                };
            });
        });

        await shield.ExecuteAsync(_ =>
        {
            originalCalls++;
            return ValueTask.FromException(new InvalidOperationException("primary"));
        });

        await Assert.That(originalCalls).IsEqualTo(1);
        await Assert.That(generatedCalls).IsEqualTo(2);
    }

    [Test]
    public async Task Async_Hook_Is_Awaited_Before_Generation_And_Action()
    {
        var order = new List<string>();
        var hookStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.OnHedge = _ => order.Add("sync-hook");
            options.OnHedgeAsync = async _ =>
            {
                order.Add("async-hook-start");
                hookStarted.TrySetResult();
                await releaseHook.Task;
                order.Add("async-hook-end");
            };
            options.ActionGenerator = HedgeActionGenerator.Create<int>(_ =>
            {
                order.Add("generator");
                return _ =>
                {
                    order.Add("action");
                    return new ValueTask<int>(42);
                };
            });
        });

        var execution = shield.ExecuteAsync<int>(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary"))).AsTask();

        await hookStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(order.SequenceEqual(["sync-hook", "async-hook-start"])).IsTrue();
        releaseHook.SetResult();

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(order.SequenceEqual(
            ["sync-hook", "async-hook-start", "async-hook-end", "generator", "action"])).IsTrue();
    }

    [Test]
    public async Task Async_Hook_Failure_Preserves_Identity_And_Cancels_Primary()
    {
        var expected = new ApplicationException("hook");
        var primaryCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = TimeSpan.Zero;
            options.OnHedgeAsync = async _ =>
            {
                await Task.Yield();
                throw expected;
            };
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(async token =>
        {
            using var registration = token.Register(() => primaryCancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        });

        await primaryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(ReferenceEquals(outcome.Exception, expected)).IsTrue();
    }

    [Test]
    public async Task Caller_Cancellation_During_Async_Hook_Suppresses_Generation()
    {
        using var cancellation = new CancellationTokenSource();
        var generated = false;
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = TimeSpan.Zero;
            options.OnHedgeAsync = _ =>
            {
                cancellation.Cancel();
                return ValueTask.CompletedTask;
            };
            options.ActionGenerator = HedgeActionGenerator.Create<int>(_ =>
            {
                generated = true;
                return null;
            });
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(async token =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token);

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(generated).IsFalse();
    }

    [Test]
    public async Task Generated_Failures_Are_Judged_Before_A_Later_Action_Wins()
    {
        var expected = new InvalidOperationException("generated");
        var shield = Shield.When<InvalidOperationException>().Hedge(options =>
        {
            options.MaxAttempts = 3;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(hedge =>
                hedge.Attempt == 2
                    ? _ => ValueTask.FromException<int>(expected)
                    : _ => new ValueTask<int>(42));
        });

        var result = await shield.ExecuteAsync<int>(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Generator_Type_Mismatch_Is_Actionable()
    {
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<string>(_ => null);
        });

        var exception = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
                ValueTask.FromException<int>(new InvalidOperationException("primary"))))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("created for 'System.String'");
        await Assert.That(exception.Message).Contains("execution returns 'System.Int32'");
        await Assert.That(exception.Message)
            .Contains("Create the generator with the execution's result type.");
    }

    [Test]
    public async Task Generator_Failure_Preserves_Identity_And_Cancels_Primary()
    {
        var expected = new ApplicationException("generator");
        var primaryCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(_ => throw expected);
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(async token =>
        {
            using var registration = token.Register(() => primaryCancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        });

        await primaryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(ReferenceEquals(outcome.Exception, expected)).IsTrue();
    }

    [Test]
    public async Task Concurrent_Generated_Attempts_Are_Isolated_And_Losers_Are_Cancelled()
    {
        var attemptTwoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptThreeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primaryCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loserCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWinner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contexts = new Dictionary<int, KevlarContext>();
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxAttempts = 3;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(hedge =>
            {
                contexts.Add(hedge.Attempt, hedge.Context);
                return async token =>
                {
                    if (hedge.Attempt == 2)
                    {
                        attemptTwoStarted.TrySetResult();
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            loserCancelled.TrySetResult();
                            throw;
                        }
                    }

                    attemptThreeStarted.TrySetResult();
                    await releaseWinner.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    return 42;
                };
            });
        });

        var execution = shield.ExecuteAsync(async token =>
        {
            using var registration = token.Register(() => primaryCancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        await Task.WhenAll(attemptTwoStarted.Task, attemptThreeStarted.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(ReferenceEquals(contexts[2], contexts[3])).IsFalse();

        releaseWinner.SetResult();
        await Assert.That(await execution).IsEqualTo(42);
        await Task.WhenAll(primaryCancelled.Task, loserCancelled.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Generator_May_Reenter_The_Shield()
    {
        Shield<int>? shield = null;
        var nestedResult = 0;
        shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(_ =>
            {
                nestedResult = shield!.ExecuteAsync(static _ => new ValueTask<int>(7))
                    .GetAwaiter()
                    .GetResult();
                return static _ => new ValueTask<int>(42);
            });
        });

        var result = await shield.ExecuteAsync(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(nestedResult).IsEqualTo(7);
    }

    [Test]
    public async Task Async_Hook_Context_Remains_Valid_Until_Completion()
    {
        var key = new KevlarKey<string>("request-id");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? observed = null;
        var shield = Shield.Use(new PropertySeedingStrategy(key, "abc-123"))
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.OnHedgeAsync = async hedge =>
                {
                    await release.Task;
                    observed = hedge.Context.Properties.GetOrDefault(key, "missing");
                };
            });

        var execution = shield.ExecuteAsync<int>(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary"))).AsTask();
        release.SetResult();

        _ = await Assert.That(async () => await execution).Throws<InvalidOperationException>();
        await Assert.That(observed).IsEqualTo("abc-123");
    }

    [Test]
    public async Task Generator_Factories_Reject_Null_Delegates()
    {
        _ = await Assert.That(() => HedgeActionGenerator.Create<int>(null!))
            .Throws<ArgumentNullException>();
        _ = await Assert.That(() => HedgeActionGenerator.Create(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Void_Null_Generated_Action_Runs_Original_Action()
    {
        var attempts = 0;
        var generatedAttempts = new List<int>();
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create(hedge =>
            {
                generatedAttempts.Add(hedge.Attempt);
                return null;
            });
        });

        await shield.ExecuteAsync(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                return ValueTask.FromException(new InvalidOperationException("primary"));
            }

            return ValueTask.CompletedTask;
        });

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(generatedAttempts).IsEquivalentTo([2]);
    }

    [Test]
    public async Task Cancellation_From_Generator_Prevents_Generated_Action_From_Starting()
    {
        using var cancellation = new CancellationTokenSource();
        var actionStarted = false;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(_ =>
            {
                cancellation.Cancel();
                return _ =>
                {
                    actionStarted = true;
                    return new ValueTask<int>(42);
                };
            });
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")), cancellation.Token);

        await Assert.That(actionStarted).IsFalse();
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Synchronous_Generated_Action_Failure_Preserves_Identity()
    {
        var expected = new ApplicationException("generated-sync");
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(_ =>
                _ => throw expected);
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")));

        await Assert.That(ReferenceEquals(outcome.Exception, expected)).IsTrue();
    }

    [Test]
    public async Task Asynchronous_Generated_Action_Failure_Preserves_Identity()
    {
        var expected = new ApplicationException("generated-async");
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(_ => async _ =>
            {
                await Task.Yield();
                throw expected;
            });
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")));

        await Assert.That(ReferenceEquals(outcome.Exception, expected)).IsTrue();
    }

    [Test]
    public async Task Generated_Action_Can_Invoke_Synchronously_Completed_Original_Action()
    {
        var attempts = 0;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(hedge =>
                async token => (await hedge.OriginalAction(token)) + 1);
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            return attempt == 1
                ? ValueTask.FromException<int>(new InvalidOperationException("primary"))
                : new ValueTask<int>(41);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Generated_Action_Can_Invoke_Asynchronously_Completed_Original_Action()
    {
        var attempts = 0;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(hedge =>
                async token => (await hedge.OriginalAction(token)) + 1);
        });

        var result = await shield.ExecuteAsync(async _ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            await Task.Yield();
            if (attempt == 1)
            {
                throw new InvalidOperationException("primary");
            }

            return 41;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Void_Generated_Action_Is_Awaited_To_Completion()
    {
        var actionCompleted = false;
        var shield = Shield.Hedge(options =>
        {
            options.MaxAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create(_ => async _ =>
            {
                await Task.Yield();
                actionCompleted = true;
            });
        });

        await shield.ExecuteAsync(static _ =>
            ValueTask.FromException(new InvalidOperationException("primary")));

        await Assert.That(actionCompleted).IsTrue();
    }

    private sealed class PropertyObserver(KevlarKey<int> key) : Strategy
    {
        public List<int> Values { get; } = [];

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Values.Add(context.Properties.GetOrDefault(key, -1));
            return await next.InvokeAsync(context);
        }
    }

    private sealed class PropertySeedingStrategy(KevlarKey<string> key, string value) : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            context.Properties.Set(key, value);
            return next.InvokeAsync(context);
        }
    }
}

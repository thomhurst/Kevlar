using System.Collections.Concurrent;

namespace Kevlar.Tests;

public class HedgingActionGeneratorTests
{
    private static readonly KevlarKey<int> ConcurrentOriginalAction = new("concurrent-original-action");
    private static readonly KevlarKey<int> GeneratedContextRemoval = new("generated-context-removal");
    private static readonly KevlarKey<int> GeneratedContextWrite = new("generated-context-write");
    private static readonly KevlarKey<int> OriginalContextWrite = new("original-context-write");
    private static readonly KevlarKey<ThrowingEquality> ThrowingEqualityKey = new("throwing-equality");

    [Test]
    public async Task Typed_Generator_Selects_A_Distinct_Action()
    {
        var primaryCalls = 0;
        var generatedAttempts = new List<int>();
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
            {
                generatedAttempts.Add(hedge.AttemptNumber);
                return _ => new ValueTask<int>(hedge.AttemptNumber * 10);
            };
        });

        var result = await shield.ExecuteAsync<int>(_ =>
        {
            primaryCalls++;
            throw new InvalidOperationException("primary");
        });

        await Assert.That(result).IsEqualTo(10);
        await Assert.That(primaryCalls).IsEqualTo(1);
        await Assert.That(generatedAttempts).IsEquivalentTo([1]);
    }

    [Test]
    public async Task Typed_Generator_Is_Called_Once_For_Each_Additional_Attempt()
    {
        var generatedAttempts = new List<int>();
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 3;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
            {
                generatedAttempts.Add(hedge.AttemptNumber);
                return hedge.AttemptNumber == 3
                    ? static _ => new ValueTask<int>(42)
                    : static _ => ValueTask.FromException<int>(new InvalidOperationException());
            };
        });

        var result = await shield.ExecuteAsync(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException()));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(generatedAttempts).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task Typed_Generator_Receives_The_Latest_Outcome_And_Attempt_Context()
    {
        var expected = new InvalidOperationException("primary");
        var key = new KevlarKey<string>("request-id");
        Outcome<int>? observedOutcome = null;
        var observedAttempt = 0;
        string? observedProperty = null;
        var shield = Shield.For<int>()
            .Use(new PropertySeedingStrategy(key, "abc-123"))
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = hedge =>
                {
                    observedOutcome = hedge.Outcome;
                    observedAttempt = hedge.AttemptNumber;
                    observedProperty = hedge.Context.Properties.GetOrDefault(key, "missing");
                    return static _ => new ValueTask<int>(42);
                };
            });

        var result = await shield.ExecuteAsync(
            expected,
            static (failure, _) => ValueTask.FromException<int>(failure));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedOutcome.HasValue).IsTrue();
        await Assert.That(ReferenceEquals(observedOutcome!.Value.Exception, expected)).IsTrue();
        await Assert.That(observedAttempt).IsEqualTo(1);
        await Assert.That(observedProperty).IsEqualTo("abc-123");
    }

    [Test]
    public async Task Typed_Generator_Outcome_Is_Null_When_The_Primary_Is_Still_Pending()
    {
        Outcome<int>? observedOutcome = default;
        var primaryCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = hedge =>
            {
                observedOutcome = hedge.Outcome;
                return static _ => new ValueTask<int>(42);
            };
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            using var registration = token.Register(() => primaryCancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedOutcome.HasValue).IsFalse();
        await primaryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Null_Generated_Action_Runs_The_Original_Inner_Pipeline()
    {
        var key = new KevlarKey<int>("hedge-target");
        var observer = new PropertyObserver(key);
        var attempts = 0;
        var shield = Shield.Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = HedgeActionGenerator.Create<int>(hedge =>
                {
                    hedge.Context.Properties.Set(key, hedge.AttemptNumber);
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
        await Assert.That(observer.Values).IsEquivalentTo([-1, 1]);
    }

    [Test]
    public async Task Untyped_Void_Generator_Selects_A_Distinct_Action()
    {
        var originalCalls = 0;
        var generatedCalls = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create(hedge =>
            {
                return _ =>
                {
                    generatedCalls += hedge.AttemptNumber;
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
        await Assert.That(generatedCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Async_Hook_Is_Awaited_Before_Generation_And_Action()
    {
        var order = new List<string>();
        var hookStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.OnHedge = async _ =>
            {
                order.Add("hook-start");
                hookStarted.TrySetResult();
                await releaseHook.Task;
                order.Add("hook-end");
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
        await Assert.That(order.SequenceEqual(["hook-start"])).IsTrue();
        releaseHook.SetResult();

        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(order.SequenceEqual(
            ["hook-start", "hook-end", "generator", "action"])).IsTrue();
    }

    [Test]
    public async Task Async_Hook_Failure_Does_Not_Block_The_Hedge()
    {
        var expected = new ApplicationException("hook");
        var primaryCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = async _ =>
            {
                await Task.Yield();
                throw expected;
            };
        });

        var attempts = 0;
        var result = await shield.ExecuteAsync<int>(async token =>
        {
            if (Interlocked.Increment(ref attempts) != 1)
            {
                return 42;
            }

            using var registration = token.Register(() => primaryCancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        });

        await primaryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Synchronously_Faulted_Async_Hook_Preserves_Action_Failure()
    {
        var expected = new ApplicationException("hook");
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.OnHedge = _ => ValueTask.FromException(expected);
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")));

        await Assert.That(outcome.Exception).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task Caller_Cancellation_During_Async_Hook_Suppresses_Generation()
    {
        using var cancellation = new CancellationTokenSource();
        var generated = false;
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ =>
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
            options.MaxHedgedAttempts = 2;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<int>(hedge =>
                hedge.AttemptNumber == 1
                    ? _ => ValueTask.FromException<int>(expected)
                    : _ => new ValueTask<int>(42));
        });

        var result = await shield.ExecuteAsync<int>(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Untyped_Generator_Type_Mismatch_Fails_When_Lifted()
    {
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create<string>(_ => null);
        });

        var exception = await Assert.That(() => shield.For<int>())
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("created for 'System.String'");
        await Assert.That(exception.Message).Contains("shield returns 'System.Int32'");
        await Assert.That(exception.Message)
            .Contains("Create the generator with the shield's result type.");
    }

    [Test]
    public async Task Typed_Generator_Exception_Becomes_An_Attempt_Outcome_And_Primary_Can_Win()
    {
        var expected = new ApplicationException("generator");
        var generatorFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrimary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = _ =>
            {
                generatorFailed.TrySetResult();
                throw expected;
            };
        });

        var execution = shield.ExecuteOutcomeAsync(async token =>
        {
            await releasePrimary.Task.WaitAsync(token);
            return 42;
        }).AsTask();

        await generatorFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releasePrimary.SetResult();

        var outcome = await execution;
        await Assert.That(outcome.Result).IsEqualTo(42);
    }

    [Test]
    public async Task Concurrent_Generated_Attempts_Are_Isolated_And_Losers_Are_Cancelled()
    {
        var attemptTwoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptThreeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primaryCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loserCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWinner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contexts = new ConcurrentDictionary<int, KevlarContext>();
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = hedge =>
            {
                contexts[hedge.AttemptNumber] = hedge.Context;
                return async token =>
                {
                    if (hedge.AttemptNumber == 1)
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
            };
        });

        var execution = shield.ExecuteAsync(async token =>
        {
            using var registration = token.Register(() => primaryCancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }).AsTask();

        await Task.WhenAll(attemptTwoStarted.Task, attemptThreeStarted.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(ReferenceEquals(contexts[1], contexts[2])).IsFalse();

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
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = _ =>
            {
                nestedResult = shield!.ExecuteAsync(static _ => new ValueTask<int>(7))
                    .GetAwaiter()
                    .GetResult();
                return static _ => new ValueTask<int>(42);
            };
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
        var hookStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? observed = null;
        var shield = Shield.Use(new PropertySeedingStrategy(key, "abc-123"))
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.OnHedge = async hedge =>
                {
                    hookStarted.TrySetResult();
                    await release.Task;
                    observed = hedge.Context.Properties.GetOrDefault(key, "missing");
                };
            });

        var execution = shield.ExecuteAsync<int>(_ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary"))).AsTask();
        await hookStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create(hedge =>
            {
                generatedAttempts.Add(hedge.AttemptNumber);
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
        await Assert.That(generatedAttempts).IsEquivalentTo([1]);
    }

    [Test]
    public async Task Cancellation_From_Generator_Prevents_Generated_Action_From_Starting()
    {
        using var cancellation = new CancellationTokenSource();
        var actionStarted = false;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
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
            options.MaxHedgedAttempts = 1;
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
            options.MaxHedgedAttempts = 1;
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
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
                async token => (await hedge.OriginalAction(token)) + 1;
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
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
                async token => (await hedge.OriginalAction(token)) + 1;
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
    public async Task Original_Action_Uses_The_Supplied_Cancellation_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var observedToken = default(CancellationToken);
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
                _ => hedge.OriginalAction(cancellation.Token);
        });

        var result = await shield.ExecuteAsync(token =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                return ValueTask.FromException<int>(new InvalidOperationException("primary"));
            }

            observedToken = token;
            return new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Async_Original_Action_Uses_The_Supplied_Cancellation_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var observedToken = default(CancellationToken);
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
                _ => hedge.OriginalAction(cancellation.Token);
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("primary");
            }

            await Task.Yield();
            observedToken = token;
            return 42;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Original_Action_Preserves_Later_Generator_Context_Writes()
    {
        var releaseOriginal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observedGeneratedProperty = 0;
        var observedOriginalProperty = 0;
        var retainedRemovedProperty = false;
        var attempts = 0;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
            {
                var original = hedge.OriginalAction(hedge.Context.CancellationToken).AsTask();
                hedge.Context.Properties.Set(GeneratedContextWrite, 42);
                hedge.Context.Properties.Remove(GeneratedContextRemoval);
                return async _ =>
                {
                    releaseOriginal.SetResult();
                    return await original;
                };
            };
        });

        var result = await shield.ExecuteWithContextAsync(
            0,
            static (_, properties) => properties.Set(GeneratedContextRemoval, 99),
            async (_, context) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("primary");
                }

                await releaseOriginal.Task;
                context.Properties.Set(OriginalContextWrite, 43);
                return 42;
            },
            (_, properties) =>
            {
                observedGeneratedProperty = properties.GetOrDefault(GeneratedContextWrite);
                observedOriginalProperty = properties.GetOrDefault(OriginalContextWrite);
                retainedRemovedProperty = properties.Contains(GeneratedContextRemoval);
            });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedGeneratedProperty).IsEqualTo(42);
        await Assert.That(observedOriginalProperty).IsEqualTo(43);
        await Assert.That(retainedRemovedProperty).IsFalse();
    }

    [Test]
    public async Task Concurrent_Original_Actions_With_The_Same_Token_Use_Distinct_Contexts()
    {
        var observer = new ContextIdentityObserver();
        var attempts = 0;
        var observedProperty = 0;
        var shield = Shield.For<int>().Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = hedge => async token =>
                {
                    var executions = Enumerable.Range(0, 8)
                        .Select(_ => hedge.OriginalAction(token).AsTask());
                    var results = await Task.WhenAll(executions);
                    return results.Sum();
                };
            })
            .Use(observer);

        var result = await shield.ExecuteWithContextAsync(
            0,
            static (_, _) => { },
            (_, context) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                context.Properties.Set(ConcurrentOriginalAction, attempt);
                return attempt == 1
                    ? ValueTask.FromException<int>(new InvalidOperationException("primary"))
                    : new ValueTask<int>(21);
            },
            (_, properties) => observedProperty =
                properties.GetOrDefault(ConcurrentOriginalAction));

        var contexts = observer.Contexts.ToArray();
        await Assert.That(result).IsEqualTo(168);
        await Assert.That(contexts.Length).IsEqualTo(9);
        await Assert.That(ReferenceEquals(contexts[1], contexts[2])).IsFalse();
        await Assert.That(observedProperty).IsBetween(2, 9);
    }

    [Test]
    [NotInParallel]
    public async Task Detached_Original_Action_Keeps_Attempt_Context_Leased()
    {
        var originalStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOriginal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, ValueTask<int>>? invokeOriginal = null;
        Task<int>? originalAction = null;
        KevlarContext? attemptContext = null;
        var attempts = 0;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
            {
                attemptContext = hedge.Context;
                invokeOriginal = hedge.OriginalAction;
                return async token =>
                {
                    originalAction = invokeOriginal!(token).AsTask();
                    await originalStarted.Task;
                    return 42;
                };
            };
        });

        var result = await shield.ExecuteAsync(async _ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("primary");
            }

            originalStarted.SetResult();
            await releaseOriginal.Task;
            return 43;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(async () => await invokeOriginal!(CancellationToken.None))
            .Throws<InvalidOperationException>();

        var rentedContexts = new ConcurrentBag<KevlarContext>();
        var allRented = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRentals = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var rentalCount = KevlarContext.PoolCapacity + 2;
        var rentalsStarted = 0;
        var rentals = Enumerable.Range(0, rentalCount)
            .Select(_ => Shield.Empty.ExecuteWithContextAsync(async context =>
            {
                rentedContexts.Add(context);
                if (Interlocked.Increment(ref rentalsStarted) == rentalCount)
                {
                    allRented.SetResult();
                }

                await releaseRentals.Task;
                return 0;
            }).AsTask())
            .ToArray();

        try
        {
            await allRented.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(rentedContexts.Any(context =>
                ReferenceEquals(context, attemptContext))).IsFalse();
        }
        finally
        {
            releaseRentals.TrySetResult();
        }

        await Task.WhenAll(rentals);

        releaseOriginal.SetResult();
        await Assert.That(await originalAction!).IsEqualTo(43);
    }

    [Test]
    public async Task Retained_Original_Action_Cannot_Run_After_Generated_Action_Completes()
    {
        Func<CancellationToken, ValueTask<int>>? originalAction = null;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
            {
                originalAction = hedge.OriginalAction;
                return static _ => new ValueTask<int>(42);
            };
        });

        var result = await shield.ExecuteAsync(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(async () => await originalAction!(CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Original_Action_Releases_Contexts_When_Property_Merge_Throws(
        bool completesAsynchronously)
    {
        var heldContexts = RentContexts(KevlarContext.PoolCapacity);
        KevlarContext[]? recycledContexts = null;

        try
        {
            KevlarContext? attemptContext = null;
            var attempts = 0;
            var shield = Shield.For<int>().Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = hedge =>
                {
                    attemptContext = hedge.Context;
                    return hedge.OriginalAction;
                };
            });

            var exception = await Assert.That(async () => await shield.ExecuteWithContextAsync(
                    0,
                    static (_, properties) =>
                        properties.Set(ThrowingEqualityKey, new ThrowingEquality()),
                    async (_, _) =>
                    {
                        if (Interlocked.Increment(ref attempts) == 1)
                        {
                            throw new InvalidOperationException("primary");
                        }

                        if (completesAsynchronously)
                        {
                            await Task.Yield();
                        }

                        return 42;
                    },
                    static (_, _) => { }))
                .Throws<InvalidOperationException>();
            await Assert.That(exception!.Message).IsEqualTo("Property equality failed.");

            recycledContexts = RentContexts(KevlarContext.PoolCapacity);

            await Assert.That(recycledContexts.Any(context =>
                ReferenceEquals(context, attemptContext))).IsTrue();
        }
        finally
        {
            if (recycledContexts is not null)
            {
                ReturnContexts(recycledContexts);
            }

            ReturnContexts(heldContexts);
        }
    }

    [Test]
    public async Task Incomplete_Original_Action_Keeps_The_Attempt_Context_Leased()
    {
        var observer = new ContextIdentityObserver();
        var releaseOriginal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? originalTask = null;
        KevlarContext? hedgeContext = null;
        var calls = 0;
        var shield = Shield.For<int>().Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = hedge =>
                {
                    hedgeContext = hedge.Context;
                    return token =>
                    {
                        originalTask = hedge.OriginalAction(token).AsTask();
                        return new ValueTask<int>(42);
                    };
                };
            })
            .Use(observer);

        var firstResult = await shield.ExecuteAsync(async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("primary");
            }

            await releaseOriginal.Task;
            return 21;
        });

        var secondResult = await shield.ExecuteAsync(static _ => new ValueTask<int>(7));
        var secondExecutionContext = observer.Contexts.ToArray()[^1];

        await Assert.That(firstResult).IsEqualTo(42);
        await Assert.That(secondResult).IsEqualTo(7);
        await Assert.That(ReferenceEquals(hedgeContext, secondExecutionContext)).IsFalse();

        releaseOriginal.SetResult();
        await Assert.That(await originalTask!).IsEqualTo(21);
    }

    [Test]
    public async Task Retained_Original_Action_Cannot_Use_A_Recycled_Attempt_Context()
    {
        var secondGenerated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, ValueTask<int>>? retainedOriginal = null;
        var generatedActions = 0;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = hedge =>
            {
                if (Interlocked.Increment(ref generatedActions) == 1)
                {
                    retainedOriginal = hedge.OriginalAction;
                    return static _ => new ValueTask<int>(42);
                }

                secondGenerated.SetResult();
                return async _ =>
                {
                    await releaseSecond.Task;
                    return 43;
                };
            };
        });

        var firstResult = await shield.ExecuteAsync(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary")));
        var secondExecution = shield.ExecuteAsync(static _ =>
            ValueTask.FromException<int>(new InvalidOperationException("primary"))).AsTask();
        await secondGenerated.Task;

        await Assert.That(async () => await retainedOriginal!(CancellationToken.None))
            .Throws<ObjectDisposedException>();

        releaseSecond.SetResult();
        await Assert.That(await secondExecution).IsEqualTo(43);
        await Assert.That(firstResult).IsEqualTo(42);
    }

    [Test]
    public async Task Void_Generated_Action_Is_Awaited_To_Completion()
    {
        var actionCompleted = false;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
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

    [Test]
    public async Task Void_Generated_Action_Can_Invoke_Original_Action()
    {
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = Timeout.InfiniteTimeSpan;
            options.ActionGenerator = HedgeActionGenerator.Create(hedge =>
                token => hedge.OriginalAction(token));
        });

        await shield.ExecuteAsync(_ => Interlocked.Increment(ref attempts) == 1
            ? ValueTask.FromException(new InvalidOperationException("primary"))
            : ValueTask.CompletedTask);

        await Assert.That(attempts).IsEqualTo(2);
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

    private sealed class ContextIdentityObserver : Strategy
    {
        private readonly TaskCompletionSource _concurrentInvocations = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invocations;

        public ConcurrentQueue<KevlarContext> Contexts { get; } = new();

        public override async ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            Contexts.Enqueue(context);
            var invocation = Interlocked.Increment(ref _invocations);
            if (invocation > 1)
            {
                if (invocation == 3)
                {
                    _concurrentInvocations.TrySetResult();
                }

                await _concurrentInvocations.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            return await next.InvokeAsync(context);
        }
    }

    private sealed class ThrowingEquality : IEquatable<ThrowingEquality>
    {
        public bool Equals(ThrowingEquality? other) =>
            throw new InvalidOperationException("Property equality failed.");

        public override bool Equals(object? obj) =>
            obj is ThrowingEquality other && Equals(other);

        public override int GetHashCode() => 0;
    }

    private static KevlarContext[] RentContexts(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => KevlarContext.Rent(
                default,
                isSynchronous: false,
                TimeProvider.System,
                shieldName: null))
            .ToArray();

    private static void ReturnContexts(IEnumerable<KevlarContext> contexts)
    {
        foreach (var context in contexts)
        {
            KevlarContext.Return(context);
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

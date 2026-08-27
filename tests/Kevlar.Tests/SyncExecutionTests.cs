using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class SyncExecutionTests
{
    [Test]
    public async Task Sync_Exhausted_Retries_Rethrow_The_Last_Exception()
    {
        var attempts = 0;
        var shield = Shield.Retry(2, Backoff.None);

        await Assert.That(() => shield.Execute<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException($"attempt {attempts}");
        })).Throws<InvalidOperationException>().WithMessage("attempt 3");
    }

    [Test]
    public async Task Sync_Retry_Delays_Block_And_Then_Complete()
    {
        var attempts = 0;
        var shield = Shield.Retry(2, Backoff.Constant(TimeSpan.FromMilliseconds(10)));

        var result = shield.Execute(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException();
            }

            return attempts;
        });

        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Sync_CircuitBreaker_Trips_And_Rejects()
    {
        var shield = Shield.CircuitBreaker(2, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 2; i++)
        {
            await Assert.That(() => shield.Execute<int>(_ => throw new InvalidOperationException()))
                .Throws<InvalidOperationException>();
        }

        await Assert.That(() => shield.Execute(_ => 1)).Throws<CircuitOpenException>();
    }

    [Test]
    public async Task Sync_RateLimit_Rejects_When_Drained()
    {
        var fakeTime = new FakeTimeProvider();
        var shield = Shield.RateLimit(2, TimeSpan.FromSeconds(10)).WithTimeProvider(fakeTime);

        await Assert.That(shield.Execute(_ => 1)).IsEqualTo(1);
        await Assert.That(shield.Execute(_ => 2)).IsEqualTo(2);

        await Assert.That(() => shield.Execute(_ => 3)).Throws<RateLimitExceededException>();
    }

    [Test]
    public async Task Sync_RateLimit_With_Queue_Blocks_Until_Permit_Is_Due()
    {
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromMilliseconds(20);
            options.QueueLimit = 1;
        });
        _ = shield.Execute(_ => 1);

        var result = shield.Execute(_ => 2);

        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Sync_RateLimit_With_Queue_Does_Not_Post_To_A_SynchronizationContext()
    {
        var time = new ControlledTimeProvider();
        var shield = Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromSeconds(1);
            options.QueueLimit = 2;
        }).WithTimeProvider(time);
        _ = shield.Execute(_ => 1);

        var execution = Task.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new ThrowingSynchronizationContext());
            try
            {
                return shield.Execute(_ => 2);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        });

        await time.WaitForTimersAsync(1);
        await Assert.That(execution.IsCompleted).IsFalse();
        time.Advance(TimeSpan.FromSeconds(1));
        time.FireTimer(0);

        await Assert.That(await execution).IsEqualTo(2);
    }

    [Test]
    public async Task Sync_Execute_Rejects_Hooks_That_Complete_Asynchronously()
    {
        var attempts = 0;
        // Released only after the assertions so the hook is guaranteed to still be pending when
        // the synchronous guard inspects it.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ => await gate.Task;
        });

        var exception = await Assert.That(() => shield.Execute<int>(_ =>
            {
                attempts++;
                throw new InvalidOperationException();
            }))
            .Throws<NotSupportedException>();

        await Assert.That(exception!.Message).Contains("RetryOptions.OnRetry");
        await Assert.That(exception.Message).Contains("Use ExecuteAsync");
        // The guard fires when the hook runs, so the first attempt happens and no retry follows.
        await Assert.That(attempts).IsEqualTo(1);
        gate.SetResult();
    }

    [Test]
    public async Task Sync_ExecuteOutcome_Captures_Hooks_That_Complete_Asynchronously()
    {
        var attempts = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 2;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ => await gate.Task;
        });

        var outcome = shield.ExecuteOutcome<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(outcome.Exception).IsTypeOf<NotSupportedException>();
        await Assert.That(outcome.Exception!.Message).Contains("RetryOptions.OnRetry");
        await Assert.That(attempts).IsEqualTo(1);
        gate.SetResult();
    }

    [Test]
    public async Task Sync_Execute_Runs_Hooks_That_Complete_Synchronously()
    {
        var delaysGenerated = 0;
        var retriesObserved = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ =>
            {
                delaysGenerated++;
                return delaysGenerated switch
                {
                    1 => default,
                    2 => new(TimeSpan.FromMilliseconds(1)),
                    _ => new ValueTask<TimeSpan?>(Task.FromResult<TimeSpan?>(TimeSpan.Zero)),
                };
            };
            options.OnRetry = retry =>
            {
                retriesObserved++;
                return retry.AttemptNumber switch
                {
                    0 => default,
                    1 => ValueTask.CompletedTask,
                    _ => new ValueTask(Task.CompletedTask),
                };
            };
        });

        var attempts = 0;
        var result = shield.Execute(_ => ++attempts < 4
            ? throw new InvalidOperationException()
            : attempts);

        await Assert.That(result).IsEqualTo(4);
        await Assert.That(delaysGenerated).IsEqualTo(3);
        await Assert.That(retriesObserved).IsEqualTo(3);
    }

    [Test]
    public async Task Sync_Zero_Retries_Never_Invoke_Hooks()
    {
        var callbackInvoked = false;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 0;
            options.DelayGenerator = async _ =>
            {
                callbackInvoked = true;
                await Task.Yield();
                return TimeSpan.Zero;
            };
            options.OnRetry = async _ =>
            {
                callbackInvoked = true;
                await Task.Yield();
            };
        });

        var result = shield.Execute(_ => 42);
        var outcome = shield.ExecuteOutcome(_ => 43);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(outcome.Result).IsEqualTo(43);
        await Assert.That(callbackInvoked).IsFalse();
    }

    [Test]
    public async Task Sync_Execute_Rejects_Fallback_Recovery_That_Does_Not_Complete_Synchronously()
    {
        var typedGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var untypedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actionInvocations = 0;
        var typed = Shield.For<int>().Fallback(_ => new ValueTask<int>(typedGate.Task));
        var untyped = Shield.Empty.Fallback(_ => new ValueTask(untypedGate.Task));

        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            var typedException = await Assert.That(() => typed.Execute(_ =>
                {
                    actionInvocations++;
                    throw new InvalidOperationException();
                }))
                .Throws<NotSupportedException>();
            var untypedException = await Assert.That(() => untyped.Execute(_ =>
                {
                    actionInvocations++;
                    throw new InvalidOperationException();
                }))
                .Throws<NotSupportedException>();

            await Assert.That(typedException!.Message).Contains("FallbackOptions<TResult>");
            await Assert.That(untypedException!.Message).Contains("FallbackOptions");
            await Assert.That(actionInvocations).IsEqualTo(2);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            typedGate.TrySetResult(42);
            untypedGate.TrySetResult();
        }
    }

    [Test]
    public async Task Sync_Execute_Runs_Fallback_Recovery_That_Completes_Synchronously()
    {
        var untypedFallbackInvoked = false;
        var typed = Shield.For<int>().Fallback(static _ => new ValueTask<int>(42));
        var untyped = Shield.Empty.Fallback(_ =>
        {
            untypedFallbackInvoked = true;
            return ValueTask.CompletedTask;
        });

        var result = typed.Execute(_ => throw new InvalidOperationException());
        untyped.Execute(_ => throw new InvalidOperationException());

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(untypedFallbackInvoked).IsTrue();
    }

    [Test]
    public async Task Sync_ExecuteOutcome_Captures_Incomplete_Fallback_Recovery()
    {
        var typedGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var untypedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var typed = Shield.For<int>().Fallback(_ => new ValueTask<int>(typedGate.Task));
        var untyped = Shield.Empty.Fallback(_ => new ValueTask(untypedGate.Task));

        try
        {
            var typedOutcome = typed.ExecuteOutcome(_ => throw new InvalidOperationException());
            var untypedOutcome = untyped.ExecuteOutcome(_ => throw new InvalidOperationException());

            await Assert.That(typedOutcome.Exception).IsTypeOf<NotSupportedException>();
            await Assert.That(untypedOutcome.Exception).IsTypeOf<NotSupportedException>();
            await Assert.That(typedOutcome.Exception!.Message).Contains("FallbackOptions<TResult>");
            await Assert.That(untypedOutcome.Exception!.Message).Contains("FallbackOptions");
        }
        finally
        {
            typedGate.TrySetResult(42);
            untypedGate.TrySetResult();
        }
    }

    [Test]
    public async Task Sync_Incomplete_Typed_Fallback_Bypasses_Outer_Retry()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actionInvocations = 0;
        var shield = Shield.For<int>()
            .Retry(1, Backoff.None)
            .When<InvalidOperationException>()
            .Fallback(_ => new ValueTask<int>(gate.Task));

        try
        {
            await Assert.That(() => shield.Execute(_ =>
                Interlocked.Increment(ref actionInvocations) == 1
                    ? throw new InvalidOperationException()
                    : 42))
                .Throws<NotSupportedException>();
            await Assert.That(actionInvocations).IsEqualTo(1);
        }
        finally
        {
            gate.TrySetResult(42);
        }
    }

    [Test]
    public async Task Sync_Incomplete_Fallback_Bypasses_Explicit_Outer_Retry()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actionInvocations = 0;
        var shield = Shield.For<int>()
            .When<NotSupportedException>()
            .Retry(1, Backoff.None)
            .When<InvalidOperationException>()
            .Fallback(_ => new ValueTask<int>(gate.Task));

        try
        {
            await Assert.That(() => shield.Execute(_ =>
                Interlocked.Increment(ref actionInvocations) == 1
                    ? throw new InvalidOperationException()
                    : 42))
                .Throws<NotSupportedException>();
            await Assert.That(actionInvocations).IsEqualTo(1);
        }
        finally
        {
            gate.TrySetResult(42);
        }
    }

    [Test]
    public async Task Sync_Incomplete_Void_Fallback_Bypasses_Outer_Retry()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actionInvocations = 0;
        var shield = Shield.Retry(1, Backoff.None)
            .When<InvalidOperationException>()
            .Fallback(_ => new ValueTask(gate.Task));

        try
        {
            await Assert.That(() => shield.Execute(_ =>
            {
                if (Interlocked.Increment(ref actionInvocations) == 1)
                {
                    throw new InvalidOperationException();
                }
            })).Throws<NotSupportedException>();
            await Assert.That(actionInvocations).IsEqualTo(1);
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Test]
    public async Task Extension_Strategy_Can_Reject_Synchronous_Execution()
    {
        var actionInvoked = false;
        var shield = Shield.Use(new ExternalAsyncOnlyStrategy());

        var exception = await Assert.That(() => shield.Execute(_ =>
            {
                actionInvoked = true;
                return 42;
            }))
            .Throws<NotSupportedException>();

        await Assert.That(exception!.Message).Contains("ExternalAsyncOnlyStrategy.Callback");
        await Assert.That(actionInvoked).IsFalse();
    }

    [Test]
    public async Task Sync_Timeout_Generator_Completing_Synchronously_Runs_Inline()
    {
        var generatorInvoked = false;
        var shield = Shield.Timeout(options =>
            options.TimeoutGenerator = _ =>
            {
                generatorInvoked = true;
                return new(TimeSpan.FromSeconds(1));
            });

        var result = shield.Execute(_ => 42);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(generatorInvoked).IsTrue();
    }

    [Test]
    public async Task Sync_Break_Duration_Generator_Completing_Synchronously_Runs_Inline()
    {
        var generatorInvoked = false;
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDurationGenerator = _ =>
            {
                generatorInvoked = true;
                return new(TimeSpan.FromSeconds(1));
            };
        });

        await Assert.That(() => shield.Execute<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(generatorInvoked).IsTrue();
    }

    [Test]
    public async Task Synchronous_Monitor_Transitions_Run_Async_Observers_On_The_Thread_Pool()
    {
        var observedContexts = new List<SynchronizationContext?>();
        var monitor = new CircuitBreakerMonitor();
        var shield = Shield.CircuitBreaker(options =>
        {
            options.ConsecutiveFailures = 1;
            options.BreakDuration = TimeSpan.FromSeconds(1);
            options.Monitor = monitor;
            options.OnStateChanged = async _ =>
            {
                observedContexts.Add(SynchronizationContext.Current);
                await Task.Yield();
            };
        });
        _ = shield;

        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            monitor.Isolate();
            monitor.Reset();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await Assert.That(observedContexts.Count).IsEqualTo(2);
        await Assert.That(observedContexts.All(static context => context is null)).IsTrue();
    }

    [Test]
    public async Task Sync_Bulkhead_Rejects_When_Full()
    {
        var shield = Shield.ConcurrencyLimit(maxConcurrency: 1);
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var occupier = shield.ExecuteAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }).AsTask();

        await started.Task;

        await Assert.That(() => shield.Execute(_ => 2)).Throws<ConcurrencyLimitExceededException>();

        gate.SetResult();
        await Assert.That(await occupier).IsEqualTo(1);
    }

    [Test]
    public async Task Sync_WhenResult_Retries()
    {
        var attempts = 0;
        var shield = Shield.For<int>().WhenResult(value => value < 0).Retry(3, Backoff.None);

        var result = shield.Execute(_ =>
        {
            attempts++;
            return attempts < 3 ? -1 : attempts;
        });

        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Sync_State_Passing_Overloads_Thread_State()
    {
        var shield = Shield.Retry(1, Backoff.None);

        var result = shield.Execute(21, static (state, _) => state * 2);
        await Assert.That(result).IsEqualTo(42);

        var sideEffect = 0;
        shield.Execute(7, (state, _) => sideEffect = state);
        await Assert.That(sideEffect).IsEqualTo(7);
    }

    [Test]
    public async Task Sync_Empty_Policy_Passes_Through()
    {
        var result = Shield.Empty.Execute(_ => 99);
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task Sync_Composed_Pipeline_Runs_End_To_End()
    {
        var attempts = 0;
        // Fallback first (outermost), retry inside it: the retries exhaust before the
        // fallback replaces the final failure.
        var shield = Shield.For<string>()
            .When<InvalidOperationException>()
            .FallbackTo("fallback")
            .Retry(2, Backoff.None);

        var result = shield.Execute(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        // Retries exhaust (3 attempts), then the fallback replaces the failure.
        await Assert.That(result).IsEqualTo("fallback");
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Sync_Exceptions_Keep_Their_Original_Stack_Trace()
    {
        var shield = Shield.Retry(0, Backoff.None);

        try
        {
            shield.Execute<int>(_ => ThrowDeep());
            throw new Exception("should not be reached");
        }
        catch (InvalidOperationException exception)
        {
            await Assert.That(exception.StackTrace!.Contains(nameof(ThrowDeep))).IsTrue();
        }
    }

    private static int ThrowDeep() => throw new InvalidOperationException("deep");

    private sealed class ExternalAsyncOnlyStrategy : Strategy
    {
        protected internal override string? SynchronousExecutionUnsupportedReason =>
            "ExternalAsyncOnlyStrategy.Callback";

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
        }
    }

    private sealed class ThrowingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            throw new InvalidOperationException("Synchronous execution posted a continuation.");

        public override void Send(SendOrPostCallback callback, object? state) =>
            throw new InvalidOperationException("Synchronous execution sent a continuation.");
    }
}

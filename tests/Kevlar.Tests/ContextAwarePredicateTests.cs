using System.Diagnostics;

namespace Kevlar.Tests;

public class ContextAwarePredicateTests
{
    private static readonly KevlarKey<bool> IsRead = new("is-read");
    private static readonly KevlarKey<int> HedgeAttempt = new("hedge-attempt");

    [Test]
    public async Task Predicate_Receives_Attempt_Number_In_Retry()
    {
        var shield = Shield
            .WhenContext(handling => handling.Exception is TimeoutException && handling.Attempt < 1)
            .OrContext(handling => handling.Exception is HttpRequestException && handling.Attempt < 3)
            .Retry(5, Backoff.None);

        await AssertAttemptsAsync<TimeoutException>(shield, expected: 2);
        await AssertAttemptsAsync<HttpRequestException>(shield, expected: 4);
    }

    [Test]
    public async Task Predicate_Receives_Context_Properties_And_Strategy_Index()
    {
        HandlingEvent observed = default;
        var shield = Shield.Timeout(TimeSpan.FromMinutes(1))
            .WhenContext(handling =>
            {
                observed = handling;
                return handling.Context.Properties.GetOrDefault(IsRead);
            })
            .Retry(1, Backoff.None);
        var attempts = 0;

        await Assert.That(async () => await shield.ExecuteWithContextAsync(
            true,
            static (isRead, properties) => properties.Set(IsRead, isRead),
            (_, _) =>
            {
                attempts++;
                return ValueTask.FromException<int>(new InvalidOperationException());
            }))
            .Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(observed.Attempt).IsEqualTo(0);
        await Assert.That(observed.StrategyIndex).IsEqualTo(1);
    }

    [Test]
    public async Task Predicate_Attempt_Is_Zero_For_Non_Attempting_Strategies()
    {
        var attempts = new List<int>();
        var breaker = Shield
            .WhenContext(handling =>
            {
                attempts.Add(handling.Attempt);
                return true;
            })
            .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromMinutes(1));

        await Assert.That(async () => await breaker.ExecuteAsync<int>(
            _ => ValueTask.FromException<int>(new InvalidOperationException())))
            .Throws<InvalidOperationException>();

        var fallback = Shield.For<int>()
            .WhenContext(handling =>
            {
                attempts.Add(handling.Attempt);
                return handling.Outcome.Exception is InvalidOperationException;
            })
            .FallbackTo(42);
        var result = await fallback.ExecuteAsync(
            _ => ValueTask.FromException<int>(new InvalidOperationException()));
        await Assert.That(result).IsEqualTo(42);

        await Assert.That(attempts).IsEquivalentTo([0, 0]);
    }

    [Test]
    public async Task Ambient_Context_Aware_Clause_Applies_To_Later_Strategies()
    {
        var attempts = 0;
        var shield = Shield
            .WhenContext(handling => handling.Context.Properties.GetOrDefault(IsRead))
            .Retry(1, Backoff.None)
            .CircuitBreaker(consecutiveFailures: 2, breakDuration: TimeSpan.FromMinutes(1));

        await Assert.That(async () => await shield.ExecuteWithContextAsync(
            true,
            static (isRead, properties) => properties.Set(IsRead, isRead),
            (_, _) =>
            {
                attempts++;
                return ValueTask.FromException<int>(new InvalidOperationException());
            }))
            .Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Context_Aware_Clause_Is_Sealed_By_Wrap_And_Compose()
    {
        var contextual = Shield
            .WhenContext(handling => handling.Exception is TimeoutException)
            .Retry(1, Backoff.None);
        var wrapped = contextual.Wrap(Shield.Empty).Retry(1, Backoff.None);
        var composed = Shield.Compose(contextual, Shield.Empty).Retry(1, Backoff.None);

        await AssertAttemptsAsync<InvalidOperationException>(wrapped, expected: 2);
        await AssertAttemptsAsync<InvalidOperationException>(composed, expected: 2);
    }

    [Test]
    public async Task Typed_Result_Predicate_Receives_Outcome_And_Hedge_Attempt()
    {
        var observedAttempts = new List<int>();
        var executions = 0;
        var shield = Shield.For<int>()
            .WhenResultContext(handling =>
            {
                observedAttempts.Add(handling.Attempt);
                return handling.Outcome.TryGetResult(out var result) && result < 0;
            })
            .Hedge(maxAttempts: 2, delay: Timeout.InfiniteTimeSpan);

        var result = await shield.ExecuteAsync(_ =>
            new ValueTask<int>(++executions == 1 ? -1 : 42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedAttempts).IsEquivalentTo([0, 1]);
    }

    [Test]
    public async Task Hedge_Predicate_Receives_Each_Attempts_Context()
    {
        var observedValues = new List<int>();
        var executions = 0;
        var shield = Shield.For<int>()
            .WhenResultContext(handling =>
            {
                observedValues.Add(handling.Context.Properties.GetOrDefault(HedgeAttempt));
                return handling.Outcome.TryGetResult(out var result) && result < 0;
            })
            .Hedge(maxAttempts: 2, delay: Timeout.InfiniteTimeSpan);

        var result = await shield.ExecuteWithContextAsync(async context =>
        {
            await Task.Yield();
            var execution = Interlocked.Increment(ref executions);
            context.Properties.Set(HedgeAttempt, execution);
            return execution == 1 ? -1 : 42;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedValues).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task Hedge_Predicate_Receives_Generated_Original_Action_Context()
    {
        using var cancellation = new CancellationTokenSource();
        var executions = 0;
        var observedValue = -1;
        var observedToken = default(CancellationToken);
        var shield = Shield.For<int>()
            .WhenResultContext(handling =>
            {
                if (handling.Attempt == 1)
                {
                    observedValue = handling.Context.Properties.GetOrDefault(HedgeAttempt, -1);
                    observedToken = handling.Context.CancellationToken;
                }

                return handling.Outcome.TryGetResult(out var result) && result < 0;
            })
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = hedge => _ => hedge.OriginalAction(cancellation.Token);
            });

        var result = await shield.ExecuteWithContextAsync(async context =>
        {
            await Task.Yield();
            var execution = Interlocked.Increment(ref executions);
            context.Properties.Set(HedgeAttempt, execution);
            return execution == 1 ? -1 : 42;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observedValue).IsEqualTo(2);
        await Assert.That(observedToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Hedge_Predicate_Context_Is_Frozen_Before_Classification()
    {
        var releaseSlowOriginal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? slowOriginal = null;
        var executions = 0;
        var observedValue = -1;
        var shield = Shield.For<int>()
            .WhenResultContext(handling =>
            {
                if (handling.Attempt == 1)
                {
                    releaseSlowOriginal.SetResult();
                    slowOriginal!.GetAwaiter().GetResult();
                    observedValue = handling.Context.Properties.GetOrDefault(HedgeAttempt, -1);
                }

                return handling.Outcome.TryGetResult(out var result) && result < 0;
            })
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = hedge => async token =>
                {
                    var selectedOriginal = hedge.OriginalAction(token);
                    slowOriginal = hedge.OriginalAction(token).AsTask();
                    return await selectedOriginal;
                };
            });

        var result = await shield.ExecuteWithContextAsync(async context =>
        {
            await Task.Yield();
            var execution = Interlocked.Increment(ref executions);
            context.Properties.Set(HedgeAttempt, execution);
            if (execution == 3)
            {
                await releaseSlowOriginal.Task;
            }

            return execution == 1 ? -1 : execution;
        });

        await Assert.That(result).IsEqualTo(2);
        await Assert.That(observedValue).IsEqualTo(2);
        await Assert.That(await slowOriginal!).IsEqualTo(3);
    }

    [Test]
    public async Task Hedge_Predicate_Context_Matches_Returned_Original_Result()
    {
        var releaseSelected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLater = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var observedValue = -1;
        var shield = Shield.For<int>()
            .WhenResultContext(handling =>
            {
                if (handling.Attempt == 1)
                {
                    observedValue = handling.Context.Properties.GetOrDefault(HedgeAttempt, -1);
                }

                return handling.Outcome.TryGetResult(out var result) && result < 0;
            })
            .Hedge(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.ActionGenerator = hedge => async token =>
                {
                    var selectedOriginal = hedge.OriginalAction(token).AsTask();
                    var laterOriginal = hedge.OriginalAction(token).AsTask();
                    releaseSelected.SetResult();
                    var selected = await selectedOriginal;
                    releaseLater.SetResult();
                    _ = await laterOriginal;
                    return selected;
                };
            });

        var result = await shield.ExecuteWithContextAsync(async context =>
        {
            var execution = Interlocked.Increment(ref executions);
            context.Properties.Set(HedgeAttempt, execution);
            if (execution == 2)
            {
                await releaseSelected.Task;
            }
            else if (execution == 3)
            {
                await releaseLater.Task;
            }

            return execution == 1 ? -1 : execution;
        });

        await Assert.That(result).IsEqualTo(2);
        await Assert.That(observedValue).IsEqualTo(2);
    }

    [Test]
    public async Task HandlesException_Context_Override_Replaces_Ambient_Clause()
    {
        var attempts = 0;
        var shield = Shield.When<InvalidOperationException>().Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.HandlesExceptionWithContext = handling =>
                handling.Exception is ArgumentException && handling.Attempt == 0;
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new ArgumentException();
        })).Throws<ArgumentException>();
        await Assert.That(attempts).IsEqualTo(2);

        attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Predicate_Exception_Is_Treated_As_Not_Handled()
    {
        var predicateFailure = new DivideByZeroException("predicate");
        var shield = Shield.WhenContext((HandlingEvent handling) => throw predicateFailure)
            .Retry(1, Backoff.None);
        var attempts = 0;

        var executionFailure = await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException("execution");
        })).Throws<InvalidOperationException>();

        await Assert.That(executionFailure).IsNotSameReferenceAs(predicateFailure);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task Trace_Listener_Exception_Does_Not_Replace_Execution_Failure()
    {
        var listener = new ThrowingTraceListener();
        Trace.Listeners.Add(listener);

        try
        {
            var shield = Shield.WhenContext((HandlingEvent _) => throw new DivideByZeroException("predicate"))
                .Retry(1, Backoff.None);
            var executionFailure = new InvalidOperationException("execution");

            var thrown = await Assert.That(async () => await shield.ExecuteAsync<int>(
                _ => throw executionFailure)).Throws<InvalidOperationException>();

            await Assert.That(thrown).IsSameReferenceAs(executionFailure);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Test]
    public async Task Predicate_Exception_Does_Not_Skip_Later_Context_Alternative()
    {
        var alternatives = 0;
        var shield = Shield
            .WhenContext((HandlingEvent _) => throw new DivideByZeroException("first"))
            .OrContext((HandlingEvent _) =>
            {
                alternatives++;
                return true;
            })
            .Retry(1, Backoff.None);
        var attempts = 0;

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException("execution");
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(alternatives).IsEqualTo(1);
    }

    private sealed class ThrowingTraceListener : TraceListener
    {
        public override void Write(string? message) => throw new InvalidOperationException("listener");

        public override void WriteLine(string? message) => throw new InvalidOperationException("listener");
    }

    [Test]
    public async Task Typed_Predicate_Exception_Does_Not_Skip_Later_Context_Alternative()
    {
        var alternatives = 0;
        var shield = Shield.For<int>()
            .WhenResultContext((HandlingEvent<int> _) => throw new DivideByZeroException("first"))
            .OrContext((HandlingEvent<int> _) =>
            {
                alternatives++;
                return true;
            })
            .Retry(1, Backoff.None);
        var attempts = 0;

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(++attempts));

        await Assert.That(result).IsEqualTo(2);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(alternatives).IsEqualTo(1);
    }

    [Test]
    public async Task Sync_Predicate_Receives_Attempt_Number()
    {
        var attempts = 0;
        var shield = Shield
            .WhenContext(handling => handling.Attempt == 0)
            .Retry(3, Backoff.None);

        await Assert.That(() => shield.Execute<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Describe_Renders_Context_Aware_Clause_As_Custom()
    {
        var shield = Shield
            .WhenContext(handling => handling.Context.Properties.GetOrDefault(IsRead))
            .Retry(1, Backoff.None);

        await Assert.That(shield.ToString()).IsEqualTo("[when custom] Retry(1, no delay)");
    }

    private static async Task AssertAttemptsAsync<TException>(Shield shield, int expected)
        where TException : Exception, new()
    {
        var attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new TException();
        })).Throws<TException>();
        await Assert.That(attempts).IsEqualTo(expected);
    }
}

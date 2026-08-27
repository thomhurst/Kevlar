using System.Threading.Tasks.Sources;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class RetryEdgeCaseTests
{
    [Test]
    public async Task Retry_Consumes_Source_Backed_Synchronous_Outcomes_Once()
    {
        var strategy = new SourceBackedResultStrategy();
        var shield = Shield.For<int>()
            .WhenResultEquals(0)
            .Retry(1, Backoff.None)
            .Use(strategy);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(strategy.Invocations).IsEqualTo(2);
    }

    [Test]
    public async Task RetryForever_Keeps_Retrying_Until_Success()
    {
        var attempts = 0;
        var shield = Shield.RetryForever(Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 25)
            {
                throw new InvalidOperationException();
            }

            return new ValueTask<int>(attempts);
        });

        await Assert.That(result).IsEqualTo(25);
    }

    [Test]
    public async Task Predicate_Handling_Retries_Only_Matching_Exceptions()
    {
        var attempts = 0;
        var shield = Shield
            .When<InvalidOperationException>(exception => exception.Message == "transient")
            .Retry(5, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException(attempts < 3 ? "transient" : "fatal");
        })).Throws<InvalidOperationException>().WithMessage("fatal");

        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task When_Retries_On_Arbitrary_Predicates()
    {
        var attempts = 0;
        var shield = Shield
            .When(exception => exception.InnerException is not null)
            .Retry(3, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new Exception("outer", new Exception("inner"));
            }

            return new ValueTask<int>(attempts);
        });

        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Derived_Exceptions_Match_A_Base_Type_Clause()
    {
        var attempts = 0;
        var shield = Shield.When<ArgumentException>().Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new ArgumentNullException("p");
            }

            return new ValueTask<int>(attempts);
        });

        await Assert.That(result).IsEqualTo(2);
    }

    [Test]
    public async Task Nested_Retries_Multiply_Attempts()
    {
        var attempts = 0;

        // The first Retry is outermost. Each of its 3 tries runs the inner retry's full
        // 3-attempt cycle: 9 delegate invocations in total.
        var shield = Shield.Retry(2, Backoff.None).Retry(2, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(9);
    }

    [Test]
    public async Task MaxDelay_Caps_The_Computed_Backoff()
    {
        var fakeTime = new FakeTimeProvider();
        var attempts = 0;
        var attemptsStarted = new AsyncCounter("max-delay attempts");
        var seenDelays = new List<TimeSpan>();
        var shield = Shield
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.Constant(TimeSpan.FromMinutes(10));
                options.MaxDelay = TimeSpan.FromSeconds(1);
                options.OnRetry = retry =>
                {
                    seenDelays.Add(retry.Delay);
                    return default;
                };
            })
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref attempts);
            attemptsStarted.Signal();
            throw new InvalidOperationException();
        }).AsTask();

        await attemptsStarted.WaitForAsync(1);
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await attemptsStarted.WaitForAsync(2);

        await Assert.That(async () => await task).Throws<InvalidOperationException>();
        await Assert.That(seenDelays).IsEquivalentTo([TimeSpan.FromSeconds(1)]);
    }

    /// <summary>
    /// A custom backoff is untrusted arithmetic: <see cref="Backoff.Custom"/> clamps its result to
    /// the runtime timer limit, and the retry's own MaxDelay caps what is left. Neither step can be
    /// skipped without a pipeline stalling on a delay it can never wait out.
    /// </summary>
    [Test]
    public async Task MaxDelay_Caps_An_Unbounded_Custom_Backoff()
    {
        var fakeTime = new FakeTimeProvider();
        var attemptsStarted = new AsyncCounter("custom-backoff attempts");
        var seenDelays = new List<TimeSpan>();
        var shield = Shield
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.Custom(static _ => TimeSpan.MaxValue);
                options.MaxDelay = TimeSpan.FromSeconds(1);
                options.OnRetry = retry =>
                {
                    seenDelays.Add(retry.Delay);
                    return default;
                };
            })
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync<int>(_ =>
        {
            attemptsStarted.Signal();
            throw new InvalidOperationException();
        }).AsTask();

        await attemptsStarted.WaitForAsync(1);
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        await attemptsStarted.WaitForAsync(2);

        await Assert.That(async () => await task).Throws<InvalidOperationException>();
        await Assert.That(seenDelays).IsEquivalentTo([TimeSpan.FromSeconds(1)]);
    }

    [Test]
    public async Task MaxDelay_Bounds_Decorrelated_Jitter_State()
    {
        var cap = TimeSpan.FromTicks(2);
        var attempts = 0;
        var seenDelays = new List<TimeSpan>();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1_000;
            options.Backoff = Backoff.Exponential(
                TimeSpan.FromTicks(1),
                jitter: Jitter.Decorrelated);
            options.MaxDelay = cap;
            options.DelayGenerator = retry =>
            {
                seenDelays.Add(retry.Delay);
                return new(TimeSpan.Zero);
            };
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts <= 1_000
                ? throw new InvalidOperationException()
                : new ValueTask<int>(attempts);
        });

        var uncappedTailDelays = seenDelays
            .Skip(500)
            .Count(delay => delay < cap);

        await Assert.That(result).IsEqualTo(1_001);
        await Assert.That(uncappedTailDelays).IsGreaterThan(50);
    }

    [Test]
    public async Task Negative_DelayGenerator_Values_Are_Ignored()
    {
        var seenDelays = new List<TimeSpan>();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ => new(TimeSpan.FromSeconds(-1));
            options.OnRetry = retry =>
            {
                seenDelays.Add(retry.Delay);
                return default;
            };
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        // The negative value is discarded, so the backoff-computed delay (zero) is kept.
        await Assert.That(seenDelays).IsEquivalentTo([TimeSpan.Zero]);
    }

    [Test]
    public async Task Null_DelayGenerator_Result_Keeps_The_Backoff_Delay()
    {
        var seenDelays = new List<TimeSpan>();
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.DelayGenerator = _ => default;
            options.OnRetry = retry =>
            {
                seenDelays.Add(retry.Delay);
                return default;
            };
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();

        await Assert.That(seenDelays).IsEquivalentTo([TimeSpan.Zero]);
    }

    [Test]
    public async Task OnRetry_Is_Awaited_Before_The_Next_Attempt()
    {
        var order = new List<string>();
        var callbackGate = new AsyncGate("OnRetry callback");
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = async _ =>
            {
                order.Add("hook-start");
                await callbackGate.EnterAsync();
                order.Add("hook-end");
            };
        });

        var execution = shield.ExecuteAsync<int>(_ =>
        {
            order.Add("attempt");
            throw new InvalidOperationException();
        }).AsTask();

        await callbackGate.WaitForEntryAsync();
        await Assert.That(order).IsEquivalentTo(
            ["attempt", "hook-start"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        callbackGate.Release();

        await Assert.That(async () => await execution).Throws<InvalidOperationException>();
        await Assert.That(order).IsEquivalentTo(
            ["attempt", "hook-start", "hook-end", "attempt"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_OnRetry_Callback_That_Throws_Does_Not_Replace_The_Action_Failure()
    {
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => throw new DataMisalignedException("callback blew up");
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(4);
    }

    [Test]
    public async Task Result_Handling_Retry_Events_Carry_The_Result()
    {
        var events = new List<(object? Result, Exception? Exception)>();
        var shield = Shield.For<int>()
            .WhenResultEquals(0)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    events.Add((retry.Outcome.IsSuccess ? (object?)retry.Outcome.Result : null, retry.Outcome.Exception));
                    return default;
                };
            });

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(attempts++ == 0 ? 0 : 5));

        await Assert.That(result).IsEqualTo(5);
        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Result).IsEqualTo(0);
        await Assert.That(events[0].Exception).IsNull();
    }

    [Test]
    public async Task WhenResult_Value_Overload_Uses_Equality()
    {
        var attempts = 0;
        var shield = Shield.For<string>().WhenResultEquals("retry-me").Retry(3, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return new ValueTask<string>(attempts < 3 ? "retry-me" : "done");
        });

        await Assert.That(result).IsEqualTo("done");
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Mixed_Exception_And_Result_Clauses_Both_Retry()
    {
        var attempts = 0;
        var shield = Shield.For<int>()
            .When<InvalidOperationException>()
            .OrResultEquals(-1)
            .Retry(3, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts switch
            {
                1 => throw new InvalidOperationException(),
                2 => new ValueTask<int>(-1),
                _ => new ValueTask<int>(attempts),
            };
        });

        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Cancellation_Requested_During_The_Attempt_Prevents_A_Retry()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.Retry(5, Backoff.None);

        // The attempt fails with a handled exception, but the token is already cancelled
        // by then, so the retry loop returns the original failure instead of retrying.
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            cancellation.Cancel();
            throw new InvalidOperationException("last failure");
        }, cancellation.Token)).Throws<InvalidOperationException>().WithMessage("last failure");

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task The_Last_Failures_Exception_Is_Thrown_Not_The_First()
    {
        var attempts = 0;
        var shield = Shield.Retry(2, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw attempts < 3
                ? new InvalidOperationException($"attempt {attempts}")
                : new ApplicationException("final");
        })).Throws<ApplicationException>().WithMessage("final");
    }

    private sealed class SourceBackedResultStrategy : Strategy
    {
        private int _invocations;

        public int Invocations => Volatile.Read(ref _invocations);

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context)
        {
            var invocation = Interlocked.Increment(ref _invocations);
            var outcome = Outcome<T>.FromResult((T)(object)(invocation == 1 ? 0 : 42));
            return new ValueTask<Outcome<T>>(new SingleConsumptionSource<T>(outcome), 0);
        }
    }

    private sealed class SingleConsumptionSource<T>(Outcome<T> outcome) : IValueTaskSource<Outcome<T>>
    {
        private int _consumptionCount;

        public Outcome<T> GetResult(short token)
        {
            if (Interlocked.Increment(ref _consumptionCount) != 1)
            {
                throw new InvalidOperationException("ValueTask source consumed more than once.");
            }

            return outcome;
        }

        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) => throw new InvalidOperationException("Source completed synchronously.");
    }
}

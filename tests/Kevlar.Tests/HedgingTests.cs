using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class HedgingTests
{
    [Test]
    public async Task MaxHedgedAttempts_Defaults_To_One_Extra_Attempt()
    {
        await Assert.That(new HedgeOptions().MaxHedgedAttempts).IsEqualTo(1);
        await Assert.That(new HedgeOptions<int>().MaxHedgedAttempts).IsEqualTo(1);
    }

    [Test]
    public async Task Hedge_N_Makes_N_Plus_One_Executions_When_All_Fail()
    {
        var attempts = 0;
        var hedges = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
            options.OnHedge = _ => { hedges++; return default; };
        });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(hedges).IsEqualTo(2);
    }

    [Test]
    public async Task Retry_And_Hedge_Use_The_Same_Additional_Attempt_Counting()
    {
        var retryAttempts = 0;
        var hedgeAttempts = 0;

        await Assert.That(async () => await Shield.Retry(2).ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref retryAttempts);
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();
        await Assert.That(async () => await Shield.Hedge(2, Timeout.InfiniteTimeSpan).ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref hedgeAttempts);
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(retryAttempts).IsEqualTo(3);
        await Assert.That(hedgeAttempts).IsEqualTo(retryAttempts);
    }

    [Test]
    public async Task Hedge_Zero_Extra_Attempts_Is_Equivalent_To_No_Hedging()
    {
        var hedges = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 0;
            options.OnHedge = _ => { hedges++; return default; };
        });

        var result = shield.Execute(static _ => 42);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(hedges).IsEqualTo(0);
    }

    [Test]
    public async Task SuppressAdditionalAttempts_Skips_Hedges_And_Notification()
    {
        var attempts = 0;
        var notifications = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ => { notifications++; return default; };
        });

        var exception = await Assert.That(async () => await shield.ExecuteWithContextAsync<int, int>(
                0,
                static (_, properties) =>
                    properties.SuppressAdditionalAttempts = true,
                (_, _) =>
                {
                    attempts++;
                    throw new InvalidOperationException("original");
                }))
            .Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).IsEqualTo("original");
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(notifications).IsEqualTo(0);
    }

    [Test]
    public async Task Suppression_During_An_Async_Retry_Callback_Stops_A_Timed_Hedge()
    {
        var timeProvider = new ControlledTimeProvider();
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.When<InvalidOperationException>()
            .Hedge(1, TimeSpan.FromMinutes(1))
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = async retry =>
                {
                    retry.SuppressAdditionalAttempts();
                    callbackEntered.TrySetResult();
                    await releaseCallback.Task;
                };
            })
            .WithTimeProvider(timeProvider);
        var execution = shield.ExecuteAsync<int>(_ =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException();
        }).AsTask();
        await callbackEntered.Task;
        await timeProvider.WaitForTimersAsync(1);

        timeProvider.FireTimer(0);
        releaseCallback.TrySetResult();

        _ = await Assert.That(async () => await execution).Throws<InvalidOperationException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Suppression_During_OnHedge_Stops_The_Hedge_Operation()
    {
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var suppressionRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.When<InvalidOperationException>()
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = TimeSpan.Zero;
                options.OnHedge = async _ =>
                {
                    callbackEntered.TrySetResult();
                    await suppressionRequested.Task;
                };
            })
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    retry.SuppressAdditionalAttempts();
                    suppressionRequested.TrySetResult();
                    return default;
                };
            });

        _ = await Assert.That(async () => await shield.ExecuteAsync<int>(async _ =>
        {
            Interlocked.Increment(ref attempts);
            await callbackEntered.Task;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Suppression_During_DelayGenerator_Stops_The_First_Hedge()
    {
        var generatorEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var suppressionRequested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.When<InvalidOperationException>()
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.DelayGenerator = async _ =>
                {
                    generatorEntered.TrySetResult();
                    await suppressionRequested.Task;
                    return TimeSpan.Zero;
                };
            })
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = retry =>
                {
                    retry.SuppressAdditionalAttempts();
                    suppressionRequested.TrySetResult();
                    return default;
                };
            });

        _ = await Assert.That(async () => await shield.ExecuteAsync<int>(async _ =>
        {
            Interlocked.Increment(ref attempts);
            await generatorEntered.Task;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Detached_Loser_Suppression_Does_Not_Leak_Into_A_Reused_Context()
    {
        var loserCallbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoserCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var loserSuppressed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hedgeAttempts = 0;
        var hedge = Shield.When<InvalidOperationException>()
            .Hedge(1, TimeSpan.Zero)
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.OnRetry = async retry =>
                {
                    loserCallbackEntered.TrySetResult();
                    await releaseLoserCallback.Task;
                    retry.SuppressAdditionalAttempts();
                    loserSuppressed.TrySetResult();
                };
            });
        var winner = await hedge.ExecuteAsync<int>(async _ =>
        {
            if (Interlocked.Increment(ref hedgeAttempts) == 1)
            {
                await loserCallbackEntered.Task;
                return 42;
            }

            throw new InvalidOperationException();
        });
        await Assert.That(winner).IsEqualTo(42);

        var unrelatedAttempts = 0;
        var unrelatedStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var unrelated = Shield.Retry(1, Backoff.None).ExecuteAsync<int>(async _ =>
        {
            if (Interlocked.Increment(ref unrelatedAttempts) == 1)
            {
                unrelatedStarted.TrySetResult();
                await loserSuppressed.Task;
                throw new InvalidOperationException();
            }

            return 84;
        }).AsTask();
        await unrelatedStarted.Task;

        releaseLoserCallback.TrySetResult();

        await Assert.That(await unrelated).IsEqualTo(84);
        await Assert.That(unrelatedAttempts).IsEqualTo(2);
    }

    [Test]
    public async Task Handled_Failure_Launches_The_Next_Attempt_Immediately()
    {
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                throw new InvalidOperationException("first fails");
            }

            return new ValueTask<string>("second");
        });

        await Assert.That(result).IsEqualTo("second");
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Any_Negative_Delay_Hedges_Only_On_Handled_Failure()
    {
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.FromSeconds(-1);
        });

        var result = await shield.ExecuteAsync(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("first fails");
            }

            return new ValueTask<int>(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_OnHedge_Receives_The_Handled_Outcome()
    {
        HedgeEvent<string>? observed = null;
        var attempts = 0;
        var shield = Shield.For<string>()
            .WhenResult(static result => result == "retry")
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = Timeout.InfiniteTimeSpan;
                options.OnHedge = hedge =>
                {
                    observed = hedge;
                    return default;
                };
            });

        var result = await shield.ExecuteAsync(_ =>
            new ValueTask<string>(Interlocked.Increment(ref attempts) == 1 ? "retry" : "success"));

        await Assert.That(result).IsEqualTo("success");
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.Value.AttemptNumber).IsEqualTo(1);
        await Assert.That(observed.Value.Outcome).IsNotNull();
        await Assert.That(observed.Value.Outcome!.Value.Result).IsEqualTo("retry");
    }

    [Test]
    public async Task Typed_OnHedge_Receives_No_Outcome_When_Delay_Launches_Hedge()
    {
        HedgeEvent<int>? observed = null;
        var attempts = 0;
        var shield = Shield.For<int>().Hedge(options =>
        {
            options.Delay = TimeSpan.Zero;
            options.OnHedge = hedge =>
            {
                observed = hedge;
                return default;
            };
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return 42;
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.Value.Outcome).IsNull();
    }

    [Test]
    public async Task All_Attempts_Failing_Surfaces_The_Last_Failure()
    {
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.Delay = System.Threading.Timeout.InfiniteTimeSpan;
        });

        await Assert.That(async () => await shield.ExecuteAsync<string>(_ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            throw new InvalidOperationException($"attempt {attempt}");
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Parallel_Mode_Returns_The_Fastest_Success()
    {
        var attempts = 0;
        var slowGate = new TaskCompletionSource();
        var shield = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
        });

        var result = await shield.ExecuteAsync(async token =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                using var registration = token.Register(() => slowGate.TrySetResult());
                await slowGate.Task;
                token.ThrowIfCancellationRequested();
                return "slow";
            }

            return "fast";
        });

        await Assert.That(result).IsEqualTo("fast");
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Stagger_Delay_Launches_The_Second_Attempt_On_Schedule()
    {
        var fakeTime = new FakeTimeProvider();
        var attempts = 0;
        var hedges = new List<int>();
        var shield = Shield
            .Hedge(options =>
            {
                options.MaxHedgedAttempts = 1;
                options.Delay = TimeSpan.FromSeconds(1);
                options.OnHedge = hedge => { hedges.Add(hedge.AttemptNumber); return default; };
            })
            .WithTimeProvider(fakeTime);

        var task = shield.ExecuteAsync(async token =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            }

            return attempt;
        }).AsTask();

        await Assert.That(attempts).IsEqualTo(1);

        fakeTime.Advance(TimeSpan.FromSeconds(1));

        var result = await task;
        await Assert.That(result).IsEqualTo(2);
        await Assert.That(hedges).IsEquivalentTo([1]);
    }

    [Test]
    public async Task Synchronous_Execution_Is_Not_Supported()
    {
        var shield = Shield.Hedge(1, TimeSpan.Zero);

        await Assert.That(() => shield.Execute(_ => 1)).Throws<NotSupportedException>();
    }
}

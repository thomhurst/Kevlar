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

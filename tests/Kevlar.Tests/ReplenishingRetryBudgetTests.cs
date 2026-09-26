using System.Diagnostics.Metrics;
using Kevlar.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class ReplenishingRetryBudgetTests
{
    [Test]
    [NotInParallel]
    public async Task Cancellation_During_Hedge_Preparation_Does_Not_Consume_Allowance()
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new CancellingTimestampProvider(cancellation);
        using var listener = new MeterListener
        {
            InstrumentPublished = static (instrument, meterListener) =>
            {
                if (instrument.Name == "kevlar.hedge_attempts")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(static (_, _, _, _) => { });
        listener.Start();
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromHours(1));
        var attempts = 0;
        var outcome = await Shield.Hedge(options =>
        {
            options.Budget = budget;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ => { clock.CancelOnNextTimestamp = true; return default; };
        }).WithTimeProvider(clock).ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(new IOException());
        }, cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(budget.Tokens).IsEqualTo(1);
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Invalid_Capacity_And_Period_Are_Rejected()
    {
        await Assert.That(() => RetryBudget.CreateReplenishing(0, replenishmentPeriod: TimeSpan.FromSeconds(1)))
            .Throws<ArgumentOutOfRangeException>();
        foreach (var period in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1) })
        {
            await Assert.That(() => RetryBudget.CreateReplenishing(1, replenishmentPeriod: period))
                .Throws<ArgumentOutOfRangeException>();
        }
        await Assert.That(new RetryBudget().ReplenishmentPeriod).IsNull();
    }

    [Test]
    public async Task Named_Instance_And_Partitions_Share_Allowance_While_Separate_Instances_Are_Independent()
    {
        var time = new FakeTimeProvider();
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromSeconds(1), timeProvider: time);
        using var provider = new ServiceCollection().AddRetryBudget("dependency", budget).BuildServiceProvider();
        var registered = provider.GetRequiredKeyedService<RetryBudget>("dependency");
        await Assert.That(registered).IsSameReferenceAs(budget);
        using var partitions = new PartitionedShield<int>(_ => CreateRetry(registered));
        var attempts = 0;
        foreach (var key in new[] { 0, 1 })
        {
            _ = await partitions.GetShield(key).ExecuteOutcomeAsync<int>(_ =>
            {
                attempts++;
                return ValueTask.FromException<int>(new IOException());
            });
        }
        await Assert.That(attempts).IsEqualTo(3);
        var separate = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromSeconds(1), timeProvider: time);
        await Assert.That(separate.Tokens).IsEqualTo(1);
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(registered.Tokens).IsEqualTo(1);
    }

    [Test]
    public async Task Fixed_Windows_Refill_At_The_Original_Boundary_Without_Feedback()
    {
        var time = new FakeTimeProvider();
        var budget = RetryBudget.CreateReplenishing(2, replenishmentPeriod: TimeSpan.FromSeconds(10), timeProvider: time);
        var shield = CreateRetry(budget);
        var attempts = 0;
        _ = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(new IOException());
        });
        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(budget.Tokens).IsEqualTo(0);
        await Assert.That(budget.TokenRatio).IsEqualTo(0);
        await Assert.That(budget.ReplenishmentPeriod).IsEqualTo(TimeSpan.FromSeconds(10));
        _ = shield.Execute(static _ => 42);
        await Assert.That(budget.Tokens).IsEqualTo(0);
        time.Advance(TimeSpan.FromSeconds(9));
        await Assert.That(budget.AllowsAdditionalAttempt).IsFalse();
        time.Advance(TimeSpan.FromSeconds(6));
        await Assert.That(budget.Tokens).IsEqualTo(2);
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromSeconds(5));
        await Assert.That(budget.Tokens).IsEqualTo(2);
        time.Advance(TimeSpan.FromDays(100));
        await Assert.That(budget.Tokens).IsEqualTo(2);
    }

    [Test]
    public async Task Concurrent_Retries_And_Hedges_Cannot_Overspend_One_Allowance()
    {
        const int callers = 32;
        const int capacity = 7;
        var budget = RetryBudget.CreateReplenishing(capacity, replenishmentPeriod: TimeSpan.FromHours(1),
            timeProvider: new FakeTimeProvider());
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        var attempts = 0;
        async ValueTask WaitForAll()
        {
            if (Interlocked.Increment(ref arrivals) == callers)
            {
                ready.TrySetResult();
            }
            await ready.Task.WaitAsync(TestHelpers.DefaultTimeout);
        }
        var retry = Shield.Retry(options =>
        {
            options.Budget = budget;
            options.MaxRetries = 1;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => WaitForAll();
        });
        var hedge = Shield.Hedge(options =>
        {
            options.Budget = budget;
            options.MaxHedgedAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ => WaitForAll();
        });
        var tasks = Enumerable.Range(0, callers).Select(index =>
            (index % 2 == 0 ? retry : hedge).ExecuteOutcomeAsync<int>(_ =>
            {
                Interlocked.Increment(ref attempts);
                return ValueTask.FromException<int>(new IOException());
            }).AsTask()).ToArray();
        var outcomes = await Task.WhenAll(tasks).WaitAsync(TestHelpers.DefaultTimeout);
        foreach (var outcome in outcomes)
        {
            await Assert.That(outcome.Exception).IsTypeOf<IOException>();
        }
        await Assert.That(attempts).IsEqualTo(callers + capacity);
        await Assert.That(budget.Tokens).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Initial_Attempts_Remain_Free_When_Exhausted(bool typed, bool hedge)
    {
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromHours(1));
        _ = await CreateRetry(budget).ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        var attempts = 0;
        Func<CancellationToken, ValueTask<int>> action = _ =>
        {
            attempts++;
            return ValueTask.FromException<int>(new IOException());
        };
        Outcome<int> outcome;
        if (typed)
        {
            var shield = hedge
                ? Shield.For<int>().Hedge(options => { options.Budget = budget; options.Delay = TimeSpan.Zero; })
                : Shield.For<int>().Retry(options => { options.Budget = budget; options.Backoff = Backoff.None; });
            outcome = await shield.ExecuteOutcomeAsync(action);
        }
        else
        {
            var shield = hedge
                ? Shield.Hedge(options => { options.Budget = budget; options.Delay = TimeSpan.Zero; })
                : CreateRetry(budget);
            outcome = await shield.ExecuteOutcomeAsync(action);
        }
        await Assert.That(outcome.Exception).IsTypeOf<IOException>();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(budget.Tokens).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task Nested_Strategies_Charge_Only_Their_Own_Additional_Attempts(bool outerHedge, bool innerHedge)
    {
        const int capacity = 5;
        var budget = RetryBudget.CreateReplenishing(capacity, replenishmentPeriod: TimeSpan.FromHours(1));
        var outer = outerHedge
            ? Shield.Hedge(options => { options.Budget = budget; options.MaxHedgedAttempts = 10; options.Delay = TimeSpan.Zero; })
            : CreateRetry(budget);
        var shield = innerHedge
            ? outer.Hedge(options => { options.Budget = budget; options.MaxHedgedAttempts = 10; options.Delay = TimeSpan.Zero; })
            : outer.Retry(options => { options.Budget = budget; options.MaxRetries = 10; options.Backoff = Backoff.None; });
        var attempts = 0;
        _ = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            Interlocked.Increment(ref attempts);
            return ValueTask.FromException<int>(new IOException());
        });
        await Assert.That(attempts).IsEqualTo(capacity + 1);
        await Assert.That(budget.Tokens).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Cancellation_In_Notification_Does_Not_Consume_Allowance(bool hedge)
    {
        var budget = RetryBudget.CreateReplenishing(2, replenishmentPeriod: TimeSpan.FromHours(1));
        using var cancellation = new CancellationTokenSource();
        var shield = hedge ? Shield.Hedge(options =>
        {
            options.Budget = budget;
            options.Delay = TimeSpan.Zero;
            options.OnHedge = _ => { cancellation.Cancel(); return default; };
        }) : Shield.Retry(options =>
        {
            options.Budget = budget;
            options.Backoff = Backoff.None;
            options.OnRetry = _ => { cancellation.Cancel(); return default; };
        });
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()), cancellation.Token);
        await Assert.That(budget.Tokens).IsEqualTo(2);
    }

    [Test]
    public async Task Cancellation_During_Backoff_Does_Not_Consume_Allowance()
    {
        var time = new FakeTimeProvider();
        var budget = RetryBudget.CreateReplenishing(2, replenishmentPeriod: TimeSpan.FromHours(1), timeProvider: time);
        using var cancellation = new CancellationTokenSource();
        var shield = Shield.Retry(options =>
        {
            options.Budget = budget;
            options.Backoff = Backoff.Constant(TimeSpan.FromSeconds(1), jitter: Jitter.None);
        }).WithTimeProvider(time);
        var task = shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()), cancellation.Token).AsTask();
        cancellation.Cancel();
        var outcome = await task.WaitAsync(TestHelpers.DefaultTimeout);
        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(budget.Tokens).IsEqualTo(2);
    }

    [Test]
    public async Task Cancellation_After_Admission_Does_Not_Refund_Allowance()
    {
        var budget = RetryBudget.CreateReplenishing(2, replenishmentPeriod: TimeSpan.FromHours(1));
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        _ = await CreateRetry(budget).ExecuteOutcomeAsync<int>(token =>
        {
            if (++attempts == 2)
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled<int>(token);
            }
            return ValueTask.FromException<int>(new IOException());
        }, cancellation.Token);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(budget.Tokens).IsEqualTo(1);
    }

    [Test]
    public async Task Suppressed_Retry_Does_Not_Consume_Allowance_Or_Dispose_Its_Result()
    {
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromHours(1));
        using var value = new DisposableResult();
        var result = await Shield.For<DisposableResult>().Retry(options =>
        {
            options.Budget = budget;
            options.Backoff = Backoff.None;
            options.HandlesResult = static _ => true;
            options.OnRetry = retry => { retry.SuppressAdditionalAttempts(); return default; };
        }).ExecuteAsync(_ => new ValueTask<DisposableResult>(value));
        await Assert.That(result).IsSameReferenceAs(value);
        await Assert.That(value.Disposed).IsFalse();
        await Assert.That(budget.Tokens).IsEqualTo(1);
    }

    [Test]
    public async Task Downstream_Circuit_Rejection_Does_Not_Refund_Admitted_Attempts()
    {
        var budget = RetryBudget.CreateReplenishing(2, replenishmentPeriod: TimeSpan.FromHours(1));
        var attempts = 0;
        var outcome = await CreateRetry(budget)
            .CircuitBreaker(consecutiveFailures: 1, breakDuration: TimeSpan.FromHours(1))
            .ExecuteOutcomeAsync<int>(_ =>
            {
                attempts++;
                return ValueTask.FromException<int>(new IOException());
            });
        await Assert.That(outcome.Exception).IsTypeOf<CircuitOpenException>();
        await Assert.That(attempts).IsEqualTo(1);
        // Circuit-open rejection is terminal by default; its one admitted retry stays charged.
        await Assert.That(budget.Tokens).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hedge_Generator_Is_Free_Until_Its_Action_Is_Admitted(bool throws)
    {
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromHours(1));
        var generatedCalls = 0;
        _ = await Shield.For<int>().Hedge(options =>
        {
            options.Budget = budget;
            options.Delay = TimeSpan.Zero;
            options.ActionGenerator = _ =>
            {
                if (throws)
                {
                    throw new IOException("generator");
                }
                return _ => new ValueTask<int>(++generatedCalls);
            };
        }).ExecuteOutcomeAsync(static _ => ValueTask.FromException<int>(new IOException("primary")));
        await Assert.That(generatedCalls).IsEqualTo(throws ? 0 : 1);
        await Assert.That(budget.Tokens).IsEqualTo(throws ? 1 : 0);
    }

    [Test]
    public async Task Wall_Clock_Changes_And_Shield_Clocks_Do_Not_Move_Budget_Windows()
    {
        var clock = new FakeTimeProvider();
        var budgetClock = new AdjustableUtcProvider(clock);
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromSeconds(10), timeProvider: budgetClock);
        var shield = CreateRetry(budget).WithTimeProvider(new FakeTimeProvider());
        _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        budgetClock.UtcNow = DateTimeOffset.MaxValue;
        clock.Advance(TimeSpan.FromSeconds(9));
        await Assert.That(budget.Tokens).IsEqualTo(0);
        budgetClock.UtcNow = DateTimeOffset.MinValue;
        clock.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(budget.Tokens).IsEqualTo(1);
    }

    [Test]
    public async Task Exhaustion_Preserves_A_Running_Hedge_Contender()
    {
        var budget = RetryBudget.CreateReplenishing(1, replenishmentPeriod: TimeSpan.FromHours(1));
        var primary = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var task = Shield.Hedge(options =>
        {
            options.Budget = budget;
            options.MaxHedgedAttempts = 3;
            options.Delay = TimeSpan.Zero;
        }).ExecuteAsync(_ => Interlocked.Increment(ref attempts) == 1
            ? new ValueTask<int>(primary.Task)
            : ValueTask.FromException<int>(new IOException())).AsTask();
        try
        {
            await Assert.That(task.IsCompleted).IsFalse();
            await Assert.That(attempts).IsEqualTo(2);
            await Assert.That(budget.Tokens).IsEqualTo(0);
        }
        finally
        {
            primary.TrySetResult(42);
        }
        await Assert.That(await task.WaitAsync(TestHelpers.DefaultTimeout)).IsEqualTo(42);
    }

    private static Shield CreateRetry(RetryBudget budget) => Shield.Retry(options =>
    {
        options.Budget = budget;
        options.MaxRetries = 10;
        options.Backoff = Backoff.None;
    });

    private sealed class DisposableResult : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class AdjustableUtcProvider(FakeTimeProvider clock) : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => clock.GetTimestamp();
        public override long TimestampFrequency => clock.TimestampFrequency;
    }

    private sealed class CancellingTimestampProvider(CancellationTokenSource cancellation) : TimeProvider
    {
        internal bool CancelOnNextTimestamp { get; set; }
        public override long GetTimestamp()
        {
            if (CancelOnNextTimestamp)
            {
                CancelOnNextTimestamp = false;
                cancellation.Cancel();
            }
            return TimeProvider.System.GetTimestamp();
        }
    }
}

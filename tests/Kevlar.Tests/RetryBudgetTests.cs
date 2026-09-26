using Microsoft.Extensions.Time.Testing;

namespace Kevlar.Tests;

public class RetryBudgetTests
{
    [Test]
    public async Task Fractional_Refunds_Reach_The_Exact_Threshold_Without_Drift()
    {
        var budget = new RetryBudget(maxTokens: 2, tokenRatio: 0.1);
        var shield = Shield.Retry(options => { options.Budget = budget; options.MaxRetries = 0; });
        for (var index = 0; index < 2; index++)
        {
            _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        }
        for (var index = 0; index < 10; index++)
        {
            _ = shield.Execute(static _ => 42);
        }
        await Assert.That(budget.Tokens).IsEqualTo(1);
        await Assert.That(budget.AllowsAdditionalAttempt).IsFalse();
    }

    [Test]
    public async Task Failure_Threshold_Preserves_Outcome_And_Success_Refunds_Shared_Budget()
    {
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        var shield = Create(budget);
        var failure = new IOException("downstream");
        var attempts = 0;
        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(failure);
        });
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(ReferenceEquals(outcome.Exception, failure)).IsTrue();
        await Assert.That(budget.Tokens).IsEqualTo(2);
        await Assert.That(budget.AllowsAdditionalAttempt).IsFalse();

        var other = Create(budget);
        await Assert.That(other.Execute(static _ => 42)).IsEqualTo(42);
        await Assert.That(budget.Tokens).IsEqualTo(3);
        await Assert.That(budget.AllowsAdditionalAttempt).IsTrue();
        await Assert.That(shield.ToString()).IsEqualTo("Retry(10, no delay, budget)");
    }

    [Test]
    public async Task Handled_Results_Are_Failures_And_Unhandled_Exceptions_Do_Not_Change_Balance()
    {
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        var attempts = 0;
        var shield = Shield.For<int>().Retry(options =>
        {
            options.Budget = budget;
            options.Backoff = Backoff.None;
            options.HandlesResult = static result => result == 503;
        });
        await Assert.That(shield.Execute(_ => { attempts++; return 503; })).IsEqualTo(503);
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(budget.Tokens).IsEqualTo(2);
        _ = await shield.ExecuteOutcomeAsync(static _ => ValueTask.FromException<int>(new IOException()));
        await Assert.That(budget.Tokens).IsEqualTo(2);
    }

    [Test]
    public async Task Concurrent_Observations_Stay_Bounded_And_Include_Terminal_Attempts()
    {
        var budget = new RetryBudget(maxTokens: 100, tokenRatio: 0.5);
        var shield = Shield.Retry(options => { options.Budget = budget; options.MaxRetries = 0; });
        await Parallel.ForEachAsync(Enumerable.Range(0, 200), async (index, cancellationToken) =>
        {
            _ = await shield.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        });
        await Assert.That(budget.Tokens).IsEqualTo(0);
        Parallel.For(0, 100, _ => shield.Execute(static _ => 42));
        await Assert.That(budget.Tokens).IsEqualTo(50);
        await Assert.That(budget.AllowsAdditionalAttempt).IsFalse();
        Parallel.For(0, 200, _ => shield.Execute(static _ => 42));
        await Assert.That(budget.Tokens).IsEqualTo(100);
    }

    [Test]
    public async Task Partitions_Share_One_Feedback_Balance()
    {
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        var partitions = new PartitionedShield<int>(_ => Create(budget));
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
        await Assert.That(budget.Tokens).IsEqualTo(1);
    }

    [Test]
    public async Task Caller_Cancellation_Does_Not_Debit_The_Budget()
    {
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        using var cancellation = new CancellationTokenSource();
        var outcome = await Create(budget).ExecuteOutcomeAsync<int>(token =>
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(token);
        }, cancellation.Token);
        await Assert.That(outcome.Exception is OperationCanceledException).IsTrue();
        await Assert.That(budget.Tokens).IsEqualTo(4);
    }

    [Test]
    public async Task Budget_Is_Rechecked_After_Callback_Without_Disposing_Returned_Result()
    {
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        var other = Shield.Retry(options => { options.Budget = budget; options.MaxRetries = 0; });
        var value = new DisposableResult();
        var attempts = 0;
        var result = await Shield.For<DisposableResult>().Retry(options =>
        {
            options.Budget = budget;
            options.HandlesResult = static _ => true;
            options.Backoff = Backoff.None;
            options.OnRetry = async retryEvent =>
            {
                await Task.Yield();
                _ = await other.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
            };
        }).ExecuteAsync(_ => { attempts++; return new ValueTask<DisposableResult>(value); });
        await Assert.That(result).IsSameReferenceAs(value);
        await Assert.That(value.Disposed).IsFalse();
        await Assert.That(attempts).IsEqualTo(1);
        value.Dispose();
    }

    [Test]
    public async Task Budget_Is_Rechecked_After_Backoff_Without_Disposing_Returned_Result()
    {
        var time = new FakeTimeProvider();
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        var other = Shield.Retry(options => { options.Budget = budget; options.MaxRetries = 0; });
        var value = new DisposableResult();
        var attempts = 0;
        var pending = Shield.For<DisposableResult>().Retry(options =>
        {
            options.Budget = budget;
            options.HandlesResult = static _ => true;
            options.Backoff = Backoff.Constant(TimeSpan.FromMilliseconds(100), jitter: Jitter.None);
        }).WithTimeProvider(time).ExecuteAsync(_ =>
        {
            attempts++;
            return new ValueTask<DisposableResult>(value);
        }).AsTask();

        await Assert.That(pending.IsCompleted).IsFalse();
        _ = await other.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        time.Advance(TimeSpan.FromMilliseconds(100));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result).IsSameReferenceAs(value);
        await Assert.That(value.Disposed).IsFalse();
        await Assert.That(attempts).IsEqualTo(1);
        value.Dispose();
    }

    [Test]
    public async Task Invalid_Capacity_And_Refund_Are_Rejected()
    {
        await Assert.That(() => new RetryBudget(maxTokens: 0)).Throws<ArgumentOutOfRangeException>();
        foreach (var refund in new[] { 0, -1, 0.0001, double.NaN, double.PositiveInfinity })
        {
            await Assert.That(() => new RetryBudget(tokenRatio: refund)).Throws<ArgumentOutOfRangeException>();
        }
    }

    private sealed class DisposableResult : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static Shield Create(RetryBudget budget) => Shield.Retry(options =>
    {
        options.Budget = budget;
        options.MaxRetries = 10;
        options.Backoff = Backoff.None;
    });
}

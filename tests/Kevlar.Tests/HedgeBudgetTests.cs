namespace Kevlar.Tests;

public class HedgeBudgetTests
{
    [Test]
    public async Task Late_Loser_Failure_Updates_The_Budget_After_A_Winner_Returns()
    {
        var budget = new RetryBudget(maxTokens: 2, tokenRatio: 1);
        var loser = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.Hedge(options => { options.Budget = budget; options.Delay = TimeSpan.Zero; });
        var result = await shield.ExecuteAsync(_ => Interlocked.Increment(ref attempts) == 1
            ? new ValueTask<int>(loser.Task)
            : new ValueTask<int>(42));
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(budget.Tokens).IsEqualTo(2);
        loser.SetException(new IOException("late loser"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (budget.Tokens != 1)
        {
            await Task.Delay(1, timeout.Token);
        }
        await Assert.That(budget.AllowsAdditionalAttempt).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retry_Depletion_Suppresses_Hedges_And_Preserves_Initial_Attempt(bool typed)
    {
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        var retry = Shield.Retry(options => { options.Budget = budget; options.Backoff = Backoff.None; });
        _ = await retry.ExecuteOutcomeAsync<int>(static _ => ValueTask.FromException<int>(new IOException()));
        var attempts = 0;
        Func<CancellationToken, ValueTask<int>> operation = _ =>
        {
            attempts++;
            return ValueTask.FromException<int>(new IOException());
        };
        var outcome = typed
            ? await Shield.For<int>().Hedge(options => { options.Budget = budget; options.Delay = TimeSpan.Zero; })
                .ExecuteOutcomeAsync(operation)
            : await Shield.Hedge(options => { options.Budget = budget; options.Delay = TimeSpan.Zero; })
                .ExecuteOutcomeAsync(operation);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(outcome.Exception).IsTypeOf<IOException>();
        await Assert.That(budget.Tokens).IsEqualTo(1);
    }

    [Test]
    public async Task Exhaustion_Keeps_A_Running_Contender_And_Refunds_Its_Success()
    {
        var budget = new RetryBudget(maxTokens: 2, tokenRatio: 1);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondaryFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.Hedge(options =>
        {
            options.Budget = budget;
            options.Delay = TimeSpan.Zero;
            options.MaxHedgedAttempts = 1;
        });
        var execution = shield.ExecuteAsync(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                return new ValueTask<int>(release.Task);
            }
            secondaryFailed.TrySetResult();
            return ValueTask.FromException<int>(new IOException());
        }).AsTask();
        try
        {
            await secondaryFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(execution.IsCompleted).IsFalse();
            await Assert.That(budget.Tokens).IsEqualTo(1);
        }
        finally
        {
            release.TrySetResult(42);
        }
        await Assert.That(await execution).IsEqualTo(42);
        await Assert.That(budget.Tokens).IsEqualTo(2);
        await Assert.That(attempts).IsEqualTo(2);
    }
}

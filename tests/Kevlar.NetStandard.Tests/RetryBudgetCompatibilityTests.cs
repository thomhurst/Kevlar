namespace Kevlar.NetStandard.Tests;

public class RetryBudgetCompatibilityTests
{
    [Test]
    public async Task Async_Retry_And_Hedge_Share_Budget_On_NetStandard()
    {
        var budget = new RetryBudget(maxTokens: 4, tokenRatio: 1);
        var retry = Shield.Retry(options => { options.Budget = budget; options.Backoff = Backoff.None; });
        var attempts = 0;
        _ = await retry.ExecuteOutcomeAsync<int>(async _ =>
        {
            await Task.Yield();
            attempts++;
            throw new IOException();
        });
        await Assert.That(attempts).IsEqualTo(2);
        await Assert.That(budget.Tokens).IsEqualTo(2);
        var hedge = Shield.For<int>().Hedge(options => { options.Budget = budget; options.Delay = TimeSpan.Zero; });
        _ = await hedge.ExecuteOutcomeAsync(async _ =>
        {
            await Task.Yield();
            attempts++;
            throw new IOException();
        });
        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(budget.Tokens).IsEqualTo(1);
        await Assert.That(retry.Execute(static _ => 42)).IsEqualTo(42);
        await Assert.That(budget.Tokens).IsEqualTo(2);
    }
}

namespace Kevlar.Tests;

public class ShieldInvocationSemanticsTests
{
    [Test]
    public async Task Single_Attempt_Strategies_Report_At_Most_Once()
    {
        Shield[] shields =
        [
            Shield.Empty,
            Shield.Timeout(TimeSpan.FromSeconds(1)),
            Shield.CircuitBreaker(1, TimeSpan.FromSeconds(1)),
            Shield.ConcurrencyLimit(1),
            Shield.RateLimit(1, TimeSpan.FromSeconds(1)),
            Shield.Retry(0, Backoff.None),
            Shield.Hedge(1, TimeSpan.Zero),
        ];

        foreach (var shield in shields)
        {
            await Assert.That(shield.InvokesContinuationAtMostOnce).IsTrue();
        }

        await Assert.That(Shield.Empty.Fallback(_ => ValueTask.CompletedTask).InvokesContinuationAtMostOnce)
            .IsTrue();
    }

    [Test]
    public async Task Multi_Attempt_And_Custom_Strategies_Report_Potential_Reinvocation()
    {
        Shield[] shields =
        [
            Shield.Retry(1, Backoff.None),
            Shield.Hedge(2, TimeSpan.Zero),
            Shield.Timeout(TimeSpan.FromSeconds(1)).Retry(1, Backoff.None),
            Shield.Use(new CustomStrategy()),
        ];

        foreach (var shield in shields)
        {
            await Assert.That(shield.InvokesContinuationAtMostOnce).IsFalse();
        }
    }

    private sealed class CustomStrategy : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }
}

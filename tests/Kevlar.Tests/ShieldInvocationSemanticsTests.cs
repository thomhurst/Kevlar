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
        await Assert.That(Shield.For<int>().Fallback(0).InvokesContinuationAtMostOnce).IsTrue();
        await Assert.That(Shield.For<int>().Retry(0, Backoff.None).InvokesContinuationAtMostOnce).IsTrue();
        await Assert.That(Shield.For<int>().Hedge(1, TimeSpan.Zero).InvokesContinuationAtMostOnce).IsTrue();
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

        await Assert.That(Shield.For<int>().Retry(1, Backoff.None).InvokesContinuationAtMostOnce).IsFalse();
        await Assert.That(Shield.For<int>().Hedge(2, TimeSpan.Zero).InvokesContinuationAtMostOnce).IsFalse();
    }

    [Test]
    public async Task Custom_Strategy_Can_Declare_Single_Invocation()
    {
        var strategy = new SingleInvocationStrategy();

        await Assert.That(Shield.Use(strategy).InvokesContinuationAtMostOnce).IsTrue();
        await Assert.That(Shield<int>.Empty.Use(strategy).InvokesContinuationAtMostOnce).IsTrue();
        await Assert.That(
            Shield.Fallback(static _ => ValueTask.CompletedTask)
                .Use(strategy)
                .InvokesContinuationAtMostOnce)
            .IsTrue();
        await Assert.That(Shield.Use(strategy).Retry(1, Backoff.None).InvokesContinuationAtMostOnce)
            .IsFalse();
    }

    [Test]
    public async Task Wrap_And_Compose_Propagate_Invocation_Semantics()
    {
        var single = Shield.Use(new SingleInvocationStrategy());
        var repeated = Shield.Retry(1, Backoff.None);

        await Assert.That(single.Wrap(single).InvokesContinuationAtMostOnce).IsTrue();
        await Assert.That(single.Wrap(repeated).InvokesContinuationAtMostOnce).IsFalse();
        await Assert.That(Shield.Compose(single, single).InvokesContinuationAtMostOnce).IsTrue();
        await Assert.That(Shield.Compose(single, repeated).InvokesContinuationAtMostOnce).IsFalse();
    }

    private sealed class CustomStrategy : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }

    private sealed class SingleInvocationStrategy : Strategy
    {
        protected internal override bool InvokesContinuationAtMostOnce => true;

        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(
            Continuation<T, TState> next,
            KevlarContext context) => next.InvokeAsync(context);
    }
}

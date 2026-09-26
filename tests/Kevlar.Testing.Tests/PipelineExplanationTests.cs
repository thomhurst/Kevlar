namespace Kevlar.Testing.Tests;

public class PipelineExplanationTests
{
    [Test]
    public async Task Nested_Retries_Count_Initial_Attempts_And_Preserve_Order()
    {
        var descriptor = Shield.Retry(3, Backoff.None).Retry(3, Backoff.None).GetDescriptor();
        var explanation = descriptor.Explain();

        await Assert.That(explanation.AttemptBoundKind).IsEqualTo(AttemptBoundKind.Finite);
        await Assert.That(explanation.MaxAttempts).IsEqualTo(16L);
        await Assert.That(explanation.Strategies[0].Strategy).IsSameReferenceAs(descriptor.Strategies[0]);
        await Assert.That(explanation.Strategies[1].Index).IsEqualTo(1);
        await Assert.That(explanation.ToString()).Contains("at most 16");
        await Assert.That(explanation.ToString()).Contains("Actual attempts depend on outcomes");
    }

    [Test]
    public async Task Typed_Wrap_And_Compose_Multiply_Retry_And_Hedge_Bounds()
    {
        var retry = Shield.For<int>().Retry(3, Backoff.None);
        var hedge = Shield.For<int>().Hedge(2, TimeSpan.Zero);
        var explanations = new[]
        {
            retry.Wrap(hedge).GetDescriptor().Explain(),
            Shield<int>.Compose(hedge, retry).GetDescriptor().Explain(),
            Shield.Compose(Shield.Hedge(2, TimeSpan.Zero), Shield.Retry(3, Backoff.None)).GetDescriptor().Explain(),
        };

        foreach (var explanation in explanations)
        {
            await Assert.That(explanation.MaxAttempts).IsEqualTo(12L);
            await Assert.That(explanation.Strategies[1].EnclosingAttemptStrategies).IsEquivalentTo(new[] { 0 });
        }
        await Assert.That(explanations[0].Strategies[0].Strategy.Kind).IsEqualTo(StrategyKind.Retry);
        await Assert.That(explanations[1].Strategies[0].Strategy.Kind).IsEqualTo(StrategyKind.Hedge);
    }

    [Test]
    public async Task Timeout_Scopes_Identify_Enclosing_Attempts_And_Inner_Groups()
    {
        var explanation = Shield.Timeout(TimeSpan.FromSeconds(30))
            .Retry(3, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(5))
            .Retry(2, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(1))
            .GetDescriptor().Explain();

        await Assert.That(explanation.MaxAttempts).IsEqualTo(12L);
        await Assert.That(explanation.Strategies[0].TimeoutScope).IsEqualTo(TimeoutScope.Execution);
        await Assert.That(explanation.Strategies[2].TimeoutScope).IsEqualTo(TimeoutScope.Attempt);
        await Assert.That(explanation.Strategies[2].EnclosingAttemptStrategies).IsEquivalentTo(new[] { 1 });
        await Assert.That(explanation.Strategies[4].EnclosingAttemptStrategies).IsEquivalentTo(new[] { 1, 3 });
        await Assert.That(explanation.ToString()).Contains("covers all downstream strategies");
        await Assert.That(explanation.ToString()).Contains("not a guaranteed wall-clock completion limit");
    }

    [Test]
    public async Task Empty_And_Zero_Repeat_Pipelines_Have_One_Theoretical_Attempt()
    {
        var empty = Shield.Empty.GetDescriptor().Explain();
        await Assert.That(empty.MaxAttempts).IsEqualTo(1L);
        await Assert.That(empty.ToString()).IsEqualTo(string.Join(Environment.NewLine,
            "Theoretical protected-operation attempts: at most 1. Actual attempts depend on outcomes, cancellation, rejections, and timeouts.",
            "Order: outermost first. Counts exclude fallback delegates and user callbacks."));
        var explanation = Shield.Retry(0).Hedge(0, TimeSpan.Zero)
            .Timeout(TimeSpan.FromSeconds(1)).GetDescriptor().Explain();
        await Assert.That(explanation.MaxAttempts).IsEqualTo(1L);
        await Assert.That(explanation.Strategies[2].TimeoutScope).IsEqualTo(TimeoutScope.Execution);
    }

    [Test]
    public async Task RetryForever_Remains_Unbounded_Even_Inside_A_Timeout()
    {
        var explanation = Shield.Timeout(TimeSpan.FromSeconds(1)).RetryForever(Backoff.None)
            .GetDescriptor().Explain();

        await Assert.That(explanation.AttemptBoundKind).IsEqualTo(AttemptBoundKind.Unbounded);
        await Assert.That(explanation.MaxAttempts).IsNull();
        await Assert.That(explanation.ToString()).Contains("RetryForever");
    }

    [Test]
    public async Task Finite_Products_Report_Overflow_Without_Wrapping()
    {
        var explanation = Shield.Retry(int.MaxValue - 1, Backoff.None)
            .Retry(int.MaxValue - 1, Backoff.None).Retry(2, Backoff.None).GetDescriptor().Explain();

        await Assert.That(explanation.AttemptBoundKind).IsEqualTo(AttemptBoundKind.Overflow);
        await Assert.That(explanation.MaxAttempts).IsNull();
        await Assert.That(explanation.ToString()).Contains("exceeds Int64.MaxValue");
    }

    [Test]
    public async Task Custom_Strategies_Make_Bounds_And_Inner_Timeout_Scopes_Indeterminate()
    {
        var explanation = Shield.When<ArgumentException>().Use(clause => new CustomStrategy(clause))
            .Timeout(TimeSpan.FromSeconds(1)).RetryForever().GetDescriptor().Explain();

        await Assert.That(explanation.AttemptBoundKind).IsEqualTo(AttemptBoundKind.Indeterminate);
        await Assert.That(explanation.MaxAttempts).IsNull();
        await Assert.That(explanation.Strategies[1].TimeoutScope).IsEqualTo(TimeoutScope.Indeterminate);
        await Assert.That(explanation.Strategies[0].HandlingSource).IsEqualTo(HandlingSource.Custom);
        await Assert.That(explanation.Strategies[0].HandlingDescription).Contains("ArgumentException");
    }

    [Test]
    public async Task Generators_And_Predicates_Are_Not_Invoked_During_Inspection()
    {
        var explanation = Shield.For<int>().WhenResult(_ => throw new InvalidOperationException("predicate"))
            .Timeout(options => options.TimeoutGenerator = _ => throw new InvalidOperationException("timeout"))
            .Retry(options =>
            {
                options.MaxRetries = 3;
                options.DelayGenerator = _ => throw new InvalidOperationException("delay");
            })
            .Hedge(options =>
            {
                options.DelayGenerator = _ => throw new InvalidOperationException("hedge delay");
                options.ActionGenerator = _ => throw new InvalidOperationException("action");
            })
            .GetDescriptor().Explain();

        await Assert.That(explanation.AttemptBoundKind).IsEqualTo(AttemptBoundKind.Indeterminate);
        await Assert.That(explanation.ToString()).Contains("duration is dynamic and is not evaluated");
        await Assert.That(explanation.Strategies[1].HandlingSource).IsEqualTo(HandlingSource.Ambient);
    }

    [Test]
    public async Task Dynamic_Delays_Do_Not_Invalidate_A_Fixed_Attempt_Bound()
    {
        var explanation = Shield.Retry(options =>
            {
                options.MaxRetries = 2;
                options.DelayGenerator = _ => throw new InvalidOperationException("not called");
            })
            .Hedge(options => options.DelayGenerator = _ => throw new InvalidOperationException("not called"))
            .GetDescriptor().Explain();

        await Assert.That(explanation.MaxAttempts).IsEqualTo(6L);
    }

    [Test]
    public async Task Local_Handling_Overrides_Do_Not_Replace_Later_Ambient_Clauses()
    {
        var explanation = Shield.When<ArgumentException>()
            .Retry(options => options.HandlesException = _ => throw new InvalidOperationException("not called"))
            .Hedge(1, TimeSpan.Zero).GetDescriptor().Explain();

        await Assert.That(explanation.Strategies[0].HandlingSource).IsEqualTo(HandlingSource.LocalOverride);
        await Assert.That(explanation.Strategies[0].HandlingDescription).Contains("replace ambient");
        await Assert.That(explanation.Strategies[1].HandlingSource).IsEqualTo(HandlingSource.Ambient);
        await Assert.That(explanation.Strategies[1].HandlingDescription).Contains("ArgumentException");
        await Assert.That(Shield.Retry(1).GetDescriptor().Explain().Strategies[0].HandlingSource)
            .IsEqualTo(HandlingSource.Default);
        await Assert.That(Shield.For<int>().FallbackTo(0).GetDescriptor().Explain().Strategies[0].HandlingDescription)
            .Contains("and execution rejections");
    }

    [Test]
    public async Task Explanation_Collections_Are_Read_Only()
    {
        var explanation = Shield.Retry(1).Timeout(TimeSpan.FromSeconds(1)).GetDescriptor().Explain();
        var steps = (IList<StrategyExplanation>)explanation.Strategies;
        var indexes = (IList<int>)explanation.Strategies[1].EnclosingAttemptStrategies;

        await Assert.That(() => steps.Clear()).Throws<NotSupportedException>();
        await Assert.That(() => indexes.Clear()).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Every_Reactive_Strategy_Reports_Its_Local_Override()
    {
        var strategies = new[]
        {
            Shield.Hedge(options => options.HandlesException = _ => true).GetDescriptor().Explain().Strategies[0],
            Shield.CircuitBreaker(options => options.HandlesException = _ => true).GetDescriptor().Explain().Strategies[0],
            Shield.For<int>().FallbackTo(0, options => options.HandlesResult = _ => true)
                .GetDescriptor().Explain().Strategies[0],
        };
        foreach (var strategy in strategies)
        {
            await Assert.That(strategy.HandlingSource).IsEqualTo(HandlingSource.LocalOverride);
        }
    }

    [Test]
    public async Task Unused_Action_Generator_Does_Not_Make_Zero_Hedges_Indeterminate()
    {
        var explanation = Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 0;
            options.ActionGenerator = _ => throw new InvalidOperationException("not called");
        }).GetDescriptor().Explain();

        await Assert.That(explanation.MaxAttempts).IsEqualTo(1L);
    }

    [Test]
    public async Task Composed_Clauses_Remain_Independent_And_Custom_Metadata_Stays_Opaque()
    {
        var explanation = Shield.Compose(
            Shield.When<ArgumentException>().Retry(1),
            Shield.When<InvalidOperationException>().Retry(1)).GetDescriptor().Explain();

        await Assert.That(explanation.Strategies[0].HandlingDescription).Contains("ArgumentException");
        await Assert.That(explanation.Strategies[1].HandlingDescription).Contains("InvalidOperationException");
        var custom = Shield.Use(new CustomStrategy(HandlingClause.Default)).GetDescriptor().Explain();
        await Assert.That(custom.Strategies[0].HandlingDescription).Contains("Custom handling");
    }

    private sealed class CustomStrategy(HandlingClause handling) : Strategy
    {
        protected override HandlingClause? Handling => handling;
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context) =>
            throw new InvalidOperationException("Inspection must not execute the strategy.");
    }
}

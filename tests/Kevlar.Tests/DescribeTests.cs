using System.Globalization;

namespace Kevlar.Tests;

/// <summary>
/// Guards the <c>ToString</c>/<c>Describe</c> pipeline descriptions. These strings appear in
/// logs and diagnostics, so their shape is part of the public contract.
/// </summary>
public class DescribeTests
{
    [Test]
    public async Task Default_Retry_Describes_Its_Backoff()
    {
        await Assert.That(Shield.Retry(3).ToString())
            .IsEqualTo("Retry(3, exponential 250ms ×2, equal jitter, cap 30s)");
    }

    [Test]
    public async Task Constant_And_No_Delay_Backoffs_Describe_Themselves()
    {
        await Assert.That(Shield.Retry(2, Backoff.Constant(TimeSpan.FromSeconds(1))).ToString())
            .IsEqualTo("Retry(2, constant 1s)");
        await Assert.That(Shield.Retry(1, Backoff.None).ToString())
            .IsEqualTo("Retry(1, no delay)");
        await Assert.That(Shield.Retry(1, Backoff.Linear(TimeSpan.FromMilliseconds(500))).ToString())
            .IsEqualTo("Retry(1, linear 500ms steps)");
    }

    [Test]
    public async Task RetryForever_Is_Named_As_Such()
    {
        await Assert.That(Shield.RetryForever().ToString())
            .IsEqualTo("RetryForever(exponential 250ms ×2, equal jitter, cap 30s)");
    }

    [Test]
    public async Task A_Chain_Reads_Outermost_First()
    {
        var shield = Shield
            .Timeout(TimeSpan.FromSeconds(30))
            .Retry(3, Backoff.None)
            .CircuitBreaker(5, TimeSpan.FromSeconds(30));

        await Assert.That(shield.ToString())
            .IsEqualTo("Timeout(30s) → Retry(3, no delay) → CircuitBreaker(5 consecutive, break 30s)");
    }

    [Test]
    public async Task Sampling_Breaker_Describes_Ratio_Window_And_Throughput()
    {
        var shield = Shield.CircuitBreaker(options =>
        {
            options.FailureRatio = 0.5;
            options.MinimumThroughput = 20;
            options.SamplingWindow = TimeSpan.FromSeconds(30);
            options.BreakDuration = TimeSpan.FromSeconds(15);
        });

        await Assert.That(shield.ToString())
            .IsEqualTo("CircuitBreaker(50% over 30s, min 20, break 15s)");
    }

    [Test]
    public async Task Limiters_And_Hedge_Describe_Their_Configuration()
    {
        await Assert.That(Shield.RateLimit(100, TimeSpan.FromSeconds(1)).ToString())
            .IsEqualTo("RateLimit(100/1s)");
        await Assert.That(Shield.RateLimit(o => { o.Permits = 100; o.Window = TimeSpan.FromSeconds(1); o.QueueLimit = 20; }).ToString())
            .IsEqualTo("RateLimit(100/1s, queue 20)");
        await Assert.That(Shield.ConcurrencyLimit(10, 20).ToString())
            .IsEqualTo("ConcurrencyLimit(10, queue 20)");
        await Assert.That(Shield.ConcurrencyLimit(10).ToString())
            .IsEqualTo("ConcurrencyLimit(10)");
        await Assert.That(Shield.Hedge(2, TimeSpan.FromMilliseconds(100)).ToString())
            .IsEqualTo("Hedge(2 extra, delay 100ms)");
        await Assert.That(Shield.Hedge(options =>
        {
            options.MaxHedgedAttempts = 2;
            options.DelayGenerator = static _ => new(TimeSpan.Zero);
        }).ToString()).IsEqualTo("Hedge(2 extra, delay generator)");
    }

    [Test]
    public async Task Typed_Shields_And_Fallbacks_Describe_Themselves()
    {
        var shield = Shield.For<int>().WhenResult(0).FallbackTo(-1).Retry(1, Backoff.None);

        await Assert.That(shield.ToString()).IsEqualTo("[when result 0] Fallback(value) → Retry(1, no delay)");
    }

    [Test]
    public async Task A_Non_Default_Clause_Prefixes_The_Strategies_That_Share_It()
    {
        var shield = Shield
            .When<InvalidOperationException>()
            .Or<TimeoutExceededException>()
            .Retry(1, Backoff.None)
            .CircuitBreaker(5, TimeSpan.FromSeconds(30));

        await Assert.That(shield.ToString()).IsEqualTo(
            "[when InvalidOperationException | TimeoutExceededException] Retry(1, no delay) → "
            + "CircuitBreaker(5 consecutive, break 30s)");
    }

    [Test]
    public async Task Each_Clause_Term_Has_Its_Own_Rendering()
    {
        await Assert.That(Shield.When<InvalidOperationException>(static _ => true).Retry(1, Backoff.None).ToString())
            .IsEqualTo("[when InvalidOperationException matching predicate] Retry(1, no delay)");
        await Assert.That(Shield.When(static _ => true).Retry(1, Backoff.None).ToString())
            .IsEqualTo("[when exception predicate] Retry(1, no delay)");
        await Assert.That(Shield.For<int>().WhenResult(static _ => true).Retry(1, Backoff.None).ToString())
            .IsEqualTo("[when result predicate] Retry(1, no delay)");
        await Assert.That(Shield.For<int>().WhenResultIsDefault().Retry(1, Backoff.None).ToString())
            .IsEqualTo("[when default result] Retry(1, no delay)");
        await Assert.That(Shield.For<string?>().WhenResultIsNull().Retry(1, Backoff.None).ToString())
            .IsEqualTo("[when null result] Retry(1, no delay)");
        await Assert.That(Shield.For<string>().WhenResult("busy").Retry(1, Backoff.None).ToString())
            .IsEqualTo("[when result \"busy\"] Retry(1, no delay)");
    }

    [Test]
    public async Task Result_Literals_Are_Culture_Invariant()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            await Assert.That(Shield.For<double>().WhenResult(1.5).Retry(1, Backoff.None).ToString())
                .IsEqualTo("[when result 1.5] Retry(1, no delay)");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public async Task Proactive_Strategies_Neither_Open_Nor_Close_A_Clause_Run()
    {
        var shield = Shield
            .Timeout(TimeSpan.FromSeconds(30))
            .When<InvalidOperationException>()
            .Retry(1, Backoff.None)
            .Timeout(TimeSpan.FromSeconds(10))
            .CircuitBreaker(5, TimeSpan.FromSeconds(30));

        await Assert.That(shield.ToString()).IsEqualTo(
            "Timeout(30s) → [when InvalidOperationException] Retry(1, no delay) → Timeout(10s) → "
            + "CircuitBreaker(5 consecutive, break 30s)");
    }

    [Test]
    public async Task A_Replaced_Or_Reset_Clause_Starts_A_New_Run()
    {
        var replaced = Shield
            .When<InvalidOperationException>()
            .Retry(1, Backoff.None)
            .When<TimeoutExceededException>()
            .Retry(2, Backoff.None);

        var reset = Shield
            .When<InvalidOperationException>()
            .Retry(1, Backoff.None)
            .WhenAnyError()
            .Retry(2, Backoff.None)
            .When<InvalidOperationException>()
            .Retry(3, Backoff.None);

        await Assert.That(replaced.ToString()).IsEqualTo(
            "[when InvalidOperationException] Retry(1, no delay) → "
            + "[when TimeoutExceededException] Retry(2, no delay)");
        await Assert.That(reset.ToString()).IsEqualTo(
            "[when InvalidOperationException] Retry(1, no delay) → Retry(2, no delay) → "
            + "[when InvalidOperationException] Retry(3, no delay)");
    }

    [Test]
    public async Task A_Local_Handling_Override_Is_Marked_On_The_Strategy()
    {
        var shield = Shield
            .When<InvalidOperationException>()
            .Retry(options =>
            {
                options.MaxRetries = 1;
                options.Backoff = Backoff.None;
                options.HandlesException = static _ => true;
            })
            .CircuitBreaker(5, TimeSpan.FromSeconds(30));

        await Assert.That(shield.ToString()).IsEqualTo(
            "Retry(1, no delay) (local handling) → "
            + "[when InvalidOperationException] CircuitBreaker(5 consecutive, break 30s)");
    }

    [Test]
    public async Task A_Named_Shield_Prefixes_Its_Name()
    {
        var shield = Shield.Timeout(TimeSpan.FromSeconds(10)).WithName("github");

        await Assert.That(shield.ToString()).IsEqualTo("github: Timeout(10s)");
    }

    [Test]
    public async Task An_Empty_Shield_Says_So()
    {
        await Assert.That(Shield.Empty.ToString()).IsEqualTo("(empty)");
    }

    [Test]
    public async Task Custom_Strategies_Default_To_Their_Type_Name()
    {
        await Assert.That(Shield.Use(new NamelessStrategy()).ToString()).IsEqualTo("NamelessStrategy");
    }

    private sealed class NamelessStrategy : Strategy
    {
        public override ValueTask<Outcome<T>> ExecuteAsync<T, TState>(Continuation<T, TState> next, KevlarContext context) =>
            next.InvokeAsync(context);
    }
}

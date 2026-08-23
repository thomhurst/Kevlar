namespace Kevlar.Tests;

/// <summary>
/// Guards the build-time chain validation: a fallback chained inside a retry, hedge or breaker
/// with the same handling clause silently disables that strategy, so building such a chain throws.
/// </summary>
public class ChainValidationTests
{
    [Test]
    public async Task Fallback_Inside_A_Retry_With_The_Same_Clause_Throws()
    {
        await Assert.That(() => { _ = Shield.For<int>().When<InvalidOperationException>().Retry(2, Backoff.None).Fallback(-1); })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Fallback_Inside_A_Default_Clause_Retry_Throws()
    {
        await Assert.That(() => { _ = Shield.For<int>().Retry(2, Backoff.None).Fallback(-1); })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Fallback_Inside_A_Breaker_With_The_Same_Clause_Throws()
    {
        await Assert.That(() => { _ = Shield.For<int>().CircuitBreaker(5, TimeSpan.FromSeconds(30)).Fallback(-1); })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Void_Fallback_Inside_A_Retry_With_The_Same_Clause_Throws()
    {
        await Assert.That(() => { _ = Shield.When<InvalidOperationException>().Retry(2, Backoff.None).FallbackAction((_, _) => default); })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task The_Error_Explains_The_Fix()
    {
        InvalidOperationException? error = null;
        try
        {
            _ = Shield.For<int>().Retry(2, Backoff.None).Fallback(-1);
        }
        catch (InvalidOperationException caught)
        {
            error = caught;
        }

        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("unreachable");
        await Assert.That(error.Message).Contains("Fallback first");
    }

    [Test]
    public async Task Fallback_Before_The_Retry_Is_The_Valid_Order()
    {
        var attempts = 0;
        var shield = Shield.For<int>().Fallback(-1).Retry(2, Backoff.None);

        var recovered = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        // Fallback is outermost: it recovers only after the inner retries are exhausted.
        await Assert.That(recovered).IsEqualTo(-1);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Fallback_With_Its_Own_Narrower_Clause_Is_Allowed_Inside_A_Retry()
    {
        var attempts = 0;
        var shield = Shield.For<int>()
            .When<TimeoutExceededException>()
            .Retry(2, Backoff.None)
            .When<ArgumentException>()
            .Fallback(-1);

        // The fallback recovers ArgumentException; the retry only ever sees timeouts.
        var recovered = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            throw new ArgumentException();
        });

        await Assert.That(recovered).IsEqualTo(-1);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Recombining_Strategies_Via_Wrap_Is_Validated_Too()
    {
        var withClause = Shield.For<int>().WhenResult(0).Timeout(TimeSpan.FromMinutes(1));
        var retryPart = withClause.Retry(1, Backoff.None);
        var fallbackPart = withClause.Fallback(-1);

        await Assert.That(() => { _ = retryPart.Wrap(fallbackPart); }).Throws<InvalidOperationException>();
    }
}

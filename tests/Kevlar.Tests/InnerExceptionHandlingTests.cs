using System.IO;

namespace Kevlar.Tests;

public class InnerExceptionHandlingTests
{
    [Test]
    public async Task WhenInner_Matches_Direct_And_Nested_Exceptions()
    {
        var shield = Shield.WhenInner<IOException>().Retry(1, Backoff.None);

        await Assert.That(await CountAttemptsAsync(shield, new IOException("direct"))).IsEqualTo(2);
        await Assert.That(await CountAttemptsAsync(
            shield,
            new InvalidOperationException("outer", new IOException("nested")))).IsEqualTo(2);
    }

    [Test]
    public async Task WhenInner_Searches_Every_Aggregate_Branch()
    {
        var shield = Shield.WhenInner<IOException>().Retry(1, Backoff.None);
        var exception = new AggregateException(
            new ArgumentException("unrelated"),
            new InvalidOperationException(
                "wrapper",
                new AggregateException(
                    new TimeoutException("unrelated"),
                    new IOException("nested branch"))));

        await Assert.That(await CountAttemptsAsync(shield, exception)).IsEqualTo(2);
    }

    [Test]
    public async Task WhenInner_Handles_Deep_Ordinary_Chains_Without_Recursion()
    {
        Exception exception = new IOException("target");
        for (var index = 0; index < 50_000; index++)
        {
            exception = new InvalidOperationException("wrapper", exception);
        }

        var shield = Shield.WhenInner<IOException>().Retry(1, Backoff.None);

        await Assert.That(await CountAttemptsAsync(shield, exception)).IsEqualTo(2);
    }

    [Test]
    public async Task WhenInner_Does_Not_Match_Unrelated_Inner_Exceptions()
    {
        var shield = Shield.WhenInner<IOException>().Retry(1, Backoff.None);
        var exception = new AggregateException(
            new ArgumentException("unrelated"),
            new InvalidOperationException("outer", new TimeoutException("also unrelated")));

        await Assert.That(await CountAttemptsAsync(shield, exception)).IsEqualTo(1);
    }

    [Test]
    public async Task Inner_Predicate_Is_Applied_To_Every_Matching_Exception()
    {
        var shield = Shield
            .WhenInner<IOException>(static exception => exception.Message == "retry")
            .Retry(1, Backoff.None);
        var matching = new AggregateException(
            new IOException("skip"),
            new InvalidOperationException("wrapper", new IOException("retry")));
        var rejected = new AggregateException(new IOException("skip"));

        await Assert.That(await CountAttemptsAsync(shield, matching)).IsEqualTo(2);
        await Assert.That(await CountAttemptsAsync(shield, rejected)).IsEqualTo(1);
    }

    [Test]
    public async Task OrInner_And_Typed_WhenInner_Have_Equivalent_Semantics()
    {
        var untyped = Shield
            .When<ArgumentException>()
            .OrInner<IOException>()
            .Retry(1, Backoff.None);
        var typed = Shield.For<int>()
            .WhenInner<IOException>()
            .Or<ArgumentException>()
            .FallbackTo(42);
        var exception = new InvalidOperationException("outer", new IOException("inner"));

        await Assert.That(await CountAttemptsAsync(untyped, exception)).IsEqualTo(2);
        await Assert.That(await typed.ExecuteAsync<int>(_ => throw exception)).IsEqualTo(42);
    }

    [Test]
    public async Task Inner_Clauses_Have_Stable_Descriptions()
    {
        var plain = Shield.WhenInner<IOException>().Retry(1, Backoff.None);
        var predicate = Shield
            .When<ArgumentException>()
            .OrInner<IOException>(static _ => true)
            .Retry(1, Backoff.None);

        await Assert.That(plain.ToString())
            .IsEqualTo("[when inner IOException] Retry(1, no delay)");
        await Assert.That(predicate.ToString())
            .IsEqualTo("[when ArgumentException | inner IOException matching predicate] Retry(1, no delay)");
    }

    [Test]
    public async Task Inner_Clauses_Reject_Null_Predicates()
    {
        await Assert.That(() => Shield.WhenInner<IOException>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.Empty.WhenInner<IOException>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.When<Exception>().OrInner<IOException>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.For<int>().WhenInner<IOException>(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => Shield.For<int>().When<Exception>().OrInner<IOException>(null!)).Throws<ArgumentNullException>();
    }

    private static async Task<int> CountAttemptsAsync(Shield shield, Exception exception)
    {
        var attempts = 0;

        try
        {
            await shield.ExecuteAsync<int>(_ =>
            {
                attempts++;
                throw exception;
            });
        }
        catch (Exception caught)
        {
            await Assert.That(caught).IsSameReferenceAs(exception);
        }

        return attempts;
    }
}

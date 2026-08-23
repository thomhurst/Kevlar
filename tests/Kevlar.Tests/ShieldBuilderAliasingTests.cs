namespace Kevlar.Tests;

/// <summary>
/// Guards the snapshot taken when a <see cref="ShieldBuilder"/> or <see cref="ShieldBuilder{TResult}"/>
/// seals a clause. <c>Or…</c> accumulates into the builder and returns that same instance, so a
/// builder held in a variable stays live; a shield already built from it must keep the clause it
/// was built with, whatever is added to the builder afterwards.
/// </summary>
public class ShieldBuilderAliasingTests
{
    [Test]
    public async Task Untyped_Shield_Keeps_The_Clause_It_Was_Built_With()
    {
        var builder = Shield.When<ArgumentException>();
        var first = builder.Retry(1, Backoff.None);

        builder.Or<InvalidOperationException>();
        var second = builder.Retry(1, Backoff.None);

        var firstAttempts = 0;
        await Assert.That(async () => await first.ExecuteAsync<int>(_ =>
        {
            firstAttempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        var secondAttempts = 0;
        await Assert.That(async () => await second.ExecuteAsync<int>(_ =>
        {
            secondAttempts++;
            throw new InvalidOperationException();
        })).Throws<InvalidOperationException>();

        // The first shield never learned about InvalidOperationException, so it did not retry.
        await Assert.That(firstAttempts).IsEqualTo(1);
        await Assert.That(secondAttempts).IsEqualTo(2);
        await Assert.That(first.ToString()).IsEqualTo("[when ArgumentException] Retry(1, no delay)");
        await Assert.That(second.ToString())
            .IsEqualTo("[when ArgumentException | InvalidOperationException] Retry(1, no delay)");
    }

    [Test]
    public async Task Untyped_Single_Term_Clause_Is_Not_Extended_After_Sealing()
    {
        var builder = Shield.When<ArgumentException>();
        var first = builder.Retry(1, Backoff.None);

        builder.Or(static exception => exception is TimeoutException);

        var attempts = 0;
        await Assert.That(async () => await first.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new TimeoutException();
        })).Throws<TimeoutException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Typed_Shield_Keeps_The_Result_Clause_It_Was_Built_With()
    {
        var builder = Shield.For<int>().When<ArgumentException>();
        var first = builder.Retry(1, Backoff.None);

        builder.OrResult(0);
        var second = builder.Retry(1, Backoff.None);

        var firstAttempts = 0;
        var firstResult = await first.ExecuteAsync(_ =>
        {
            firstAttempts++;
            return new ValueTask<int>(0);
        });

        var secondAttempts = 0;
        var secondResult = await second.ExecuteAsync(_ =>
        {
            secondAttempts++;
            return new ValueTask<int>(secondAttempts == 1 ? 0 : 7);
        });

        // The first shield's clause never gained the result term, so 0 was an acceptable result.
        await Assert.That(firstResult).IsEqualTo(0);
        await Assert.That(firstAttempts).IsEqualTo(1);
        await Assert.That(secondResult).IsEqualTo(7);
        await Assert.That(secondAttempts).IsEqualTo(2);
        await Assert.That(first.ToString()).IsEqualTo("[when ArgumentException] Retry(1, no delay)");
        await Assert.That(second.ToString())
            .IsEqualTo("[when ArgumentException | result 0] Retry(1, no delay)");
    }

    [Test]
    public async Task Typed_Shield_Keeps_The_Exception_Clause_It_Was_Built_With()
    {
        var builder = Shield.For<int>().When<ArgumentException>();
        var first = builder.Retry(1, Backoff.None);

        builder.Or<InvalidOperationException>();

        var attempts = 0;
        await Assert.That(async () => await first.ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(new InvalidOperationException());
        })).Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Sealing_Twice_Without_Changes_Produces_Independent_Shields()
    {
        var builder = Shield.When<ArgumentException>().Or<InvalidOperationException>();
        var first = builder.Retry(1, Backoff.None);
        var second = builder.CircuitBreaker(2, TimeSpan.FromSeconds(1));

        await Assert.That(first.ToString())
            .IsEqualTo("[when ArgumentException | InvalidOperationException] Retry(1, no delay)");
        await Assert.That(second.ToString())
            .IsEqualTo("[when ArgumentException | InvalidOperationException] CircuitBreaker(2 consecutive, break 1s)");
    }
}

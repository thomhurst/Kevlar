namespace Kevlar.Tests;

/// <summary>
/// Guards the immutability of <see cref="ShieldBuilder"/> and <see cref="ShieldBuilder{TResult}"/>.
/// Every <c>Or…</c> returns a new builder and leaves its receiver untouched, so a builder held in a
/// variable can be branched into independent chains, a shield already built from it keeps the clause
/// it was built with, and an <c>Or…</c> whose result is discarded changes nothing at all.
/// </summary>
public class ShieldBuilderAliasingTests
{
    [Test]
    public async Task Untyped_Branching_One_Builder_Gives_Independent_Clauses()
    {
        var shared = Shield.When<ArgumentException>();
        var left = shared.Or<InvalidOperationException>().Retry(1, Backoff.None);
        var right = shared.Or<TimeoutException>().Retry(1, Backoff.None);

        await Assert.That(left.ToString())
            .IsEqualTo("[when ArgumentException | InvalidOperationException] Retry(1, no delay)");
        await Assert.That(right.ToString())
            .IsEqualTo("[when ArgumentException | TimeoutException] Retry(1, no delay)");

        // Each branch retries only the exception it added: neither inherited the other's term.
        await Assert.That(await AttemptsUntilThrow<InvalidOperationException>(left)).IsEqualTo(2);
        await Assert.That(await AttemptsUntilThrow<TimeoutException>(left)).IsEqualTo(1);
        await Assert.That(await AttemptsUntilThrow<InvalidOperationException>(right)).IsEqualTo(1);
        await Assert.That(await AttemptsUntilThrow<TimeoutException>(right)).IsEqualTo(2);
    }

    [Test]
    public async Task Untyped_Discarded_Or_Leaves_The_Builder_Unchanged()
    {
        var builder = Shield.When<ArgumentException>();

        // The returned builder is dropped, so nothing is added anywhere (the shape KEV007 flags).
        builder.Or<InvalidOperationException>();

        var shield = builder.Retry(1, Backoff.None);

        await Assert.That(shield.ToString()).IsEqualTo("[when ArgumentException] Retry(1, no delay)");
        await Assert.That(await AttemptsUntilThrow<InvalidOperationException>(shield)).IsEqualTo(1);
    }

    [Test]
    public async Task Untyped_Shield_Keeps_The_Clause_It_Was_Built_With()
    {
        var builder = Shield.When<ArgumentException>();
        var first = builder.Retry(1, Backoff.None);
        var second = builder.Or<InvalidOperationException>().Retry(1, Backoff.None);

        await Assert.That(first.ToString()).IsEqualTo("[when ArgumentException] Retry(1, no delay)");
        await Assert.That(second.ToString())
            .IsEqualTo("[when ArgumentException | InvalidOperationException] Retry(1, no delay)");

        // The first shield never learned about InvalidOperationException, so it did not retry.
        await Assert.That(await AttemptsUntilThrow<InvalidOperationException>(first)).IsEqualTo(1);
        await Assert.That(await AttemptsUntilThrow<InvalidOperationException>(second)).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_Branching_One_Builder_Gives_Independent_Clauses()
    {
        var shared = Shield.For<int>().When<ArgumentException>();
        var left = shared.OrResultEquals(0).Retry(1, Backoff.None);
        var right = shared.Or<InvalidOperationException>().Retry(1, Backoff.None);

        await Assert.That(left.ToString())
            .IsEqualTo("[when ArgumentException | result 0] Retry(1, no delay)");
        await Assert.That(right.ToString())
            .IsEqualTo("[when ArgumentException | InvalidOperationException] Retry(1, no delay)");

        // The result term went only to the left branch; the exception term only to the right.
        var leftAttempts = 0;
        var leftResult = await left.ExecuteAsync(_ =>
        {
            leftAttempts++;
            return new ValueTask<int>(leftAttempts == 1 ? 0 : 7);
        });
        await Assert.That(leftResult).IsEqualTo(7);
        await Assert.That(leftAttempts).IsEqualTo(2);

        var rightAttempts = 0;
        var rightResult = await right.ExecuteAsync(_ =>
        {
            rightAttempts++;
            return new ValueTask<int>(0);
        });
        await Assert.That(rightResult).IsEqualTo(0);
        await Assert.That(rightAttempts).IsEqualTo(1);

        await Assert.That(await TypedAttemptsUntilThrow<InvalidOperationException>(left)).IsEqualTo(1);
        await Assert.That(await TypedAttemptsUntilThrow<InvalidOperationException>(right)).IsEqualTo(2);
    }

    [Test]
    public async Task Typed_Discarded_Or_Leaves_The_Builder_Unchanged()
    {
        var builder = Shield.For<int>().When<ArgumentException>();

        // Both returned builders are dropped, so the clause stays a single term.
        builder.OrResultEquals(0);
        builder.Or<InvalidOperationException>();

        var shield = builder.Retry(1, Backoff.None);

        await Assert.That(shield.ToString()).IsEqualTo("[when ArgumentException] Retry(1, no delay)");

        var attempts = 0;
        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return new ValueTask<int>(0);
        });

        await Assert.That(result).IsEqualTo(0);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(await TypedAttemptsUntilThrow<InvalidOperationException>(shield)).IsEqualTo(1);
    }

    [Test]
    public async Task Typed_Shield_Keeps_The_Clause_It_Was_Built_With()
    {
        var builder = Shield.For<int>().When<ArgumentException>();
        var first = builder.Retry(1, Backoff.None);
        var second = builder.OrResultEquals(0).Retry(1, Backoff.None);

        await Assert.That(first.ToString()).IsEqualTo("[when ArgumentException] Retry(1, no delay)");
        await Assert.That(second.ToString())
            .IsEqualTo("[when ArgumentException | result 0] Retry(1, no delay)");

        // The first shield's clause never gained the result term, so 0 was an acceptable result.
        var firstAttempts = 0;
        var firstResult = await first.ExecuteAsync(_ =>
        {
            firstAttempts++;
            return new ValueTask<int>(0);
        });

        await Assert.That(firstResult).IsEqualTo(0);
        await Assert.That(firstAttempts).IsEqualTo(1);
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

    /// <summary>Counts the attempts a shield makes before <typeparamref name="TException"/> escapes.</summary>
    private static async Task<int> AttemptsUntilThrow<TException>(Shield shield)
        where TException : Exception, new()
    {
        var attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new TException();
        })).Throws<TException>();

        return attempts;
    }

    /// <summary>Counts the attempts a result-aware shield makes before <typeparamref name="TException"/> escapes.</summary>
    private static async Task<int> TypedAttemptsUntilThrow<TException>(Shield<int> shield)
        where TException : Exception, new()
    {
        var attempts = 0;
        await Assert.That(async () => await shield.ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.FromException<int>(new TException());
        })).Throws<TException>();

        return attempts;
    }
}

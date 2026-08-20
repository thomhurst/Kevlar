namespace Kevlar.Tests;

public class FallbackEdgeCaseTests
{
    [Test]
    public async Task A_Fallback_That_Throws_Surfaces_Its_Own_Exception()
    {
        var shield = Shield.For<int>().Fallback(_ =>
            throw new ApplicationException("fallback failed"));

        await Assert.That(async () => await shield.ExecuteAsync(_ => throw new InvalidOperationException("original")))
            .Throws<ApplicationException>().WithMessage("fallback failed");
    }

    [Test]
    public async Task Cancellation_Is_Not_Replaced_By_The_Fallback()
    {
        var shield = Shield.For<int>().Fallback(99);

        // The default handling ignores OperationCanceledException, so the fallback stays out of it.
        await Assert.That(async () => await shield.ExecuteAsync(_ => throw new OperationCanceledException()))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task The_Fallback_Receives_The_Execution_Token()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken seenToken = default;
        var shield = Shield.For<int>().Fallback((token) =>
        {
            seenToken = token;
            return new ValueTask<int>(0);
        });

        await shield.ExecuteAsync(_ => throw new InvalidOperationException(), cancellation.Token);

        await Assert.That(seenToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task FallbackEvent_Carries_The_Handled_Result()
    {
        object? seenResult = null;
        Exception? seenException = null;
        var shield = Shield.For<int>()
            .WhenResult(-1)
            .Fallback(0, fallback =>
            {
                seenResult = fallback.Outcome.Result;
                seenException = fallback.Outcome.Exception;
            });

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(result).IsEqualTo(0);
        await Assert.That(seenResult).IsEqualTo(-1);
        await Assert.That(seenException).IsNull();
    }

    [Test]
    public async Task FallbackEvent_Carries_The_Handled_Exception()
    {
        var original = new InvalidOperationException("original");
        Exception? seenException = null;
        var shield = Shield.For<int>().Fallback(0, fallback => seenException = fallback.Outcome.Exception);

        await shield.ExecuteAsync(_ => throw original);

        await Assert.That(ReferenceEquals(seenException, original)).IsTrue();
    }

    [Test]
    public async Task The_Outcome_Receiving_Fallback_Can_Branch_On_The_Failure()
    {
        var shield = Shield.For<string>().Fallback((outcome, _) =>
            new ValueTask<string>(outcome.Exception is TimeoutExceededException ? "timed-out" : "other"));

        var onTimeout = await shield.ExecuteAsync(_ => throw new TimeoutExceededException(TimeSpan.FromSeconds(1)));
        await Assert.That(onTimeout).IsEqualTo("timed-out");

        var onOther = await shield.ExecuteAsync(_ => throw new InvalidOperationException());
        await Assert.That(onOther).IsEqualTo("other");
    }

    [Test]
    public async Task Fallback_Works_On_The_Synchronous_Path()
    {
        var shield = Shield.For<string>().Fallback("fell-back");

        var result = shield.Execute(_ => throw new InvalidOperationException());

        await Assert.That(result).IsEqualTo("fell-back");
    }

    [Test]
    public async Task Fallback_Around_A_Circuit_Breaker_Swallows_Rejections()
    {
        var shield = Shield.For<int>()
            .Fallback(-1)
            .CircuitBreaker(1, TimeSpan.FromMinutes(1));

        // First execution trips the breaker; the fallback replaces the original failure.
        var first = await shield.ExecuteAsync(_ => throw new InvalidOperationException());
        await Assert.That(first).IsEqualTo(-1);

        // Later executions are rejected by the open circuit, and the fallback replaces those too.
        var second = await shield.ExecuteAsync(_ => new ValueTask<int>(1));
        await Assert.That(second).IsEqualTo(-1);
    }
}

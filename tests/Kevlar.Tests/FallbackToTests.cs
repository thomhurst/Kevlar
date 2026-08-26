namespace Kevlar.Tests;

public class FallbackToTests
{
    [Test]
    public async Task FallbackTo_Null_Returns_Null_For_Reference_Result()
    {
        var shield = Shield.For<string?>().FallbackTo(null);

        var result = await shield.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FallbackTo_Default_Struct_Returns_Default()
    {
        var shield = Shield.For<DateTime>().FallbackTo(default);

        var result = await shield.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(result).IsEqualTo(default(DateTime));
    }

    [Test]
    public async Task FallbackTo_Value_Fires_OnFallback_With_Outcome()
    {
        Exception? observed = null;
        var shield = Shield.For<int>().FallbackTo(
            42,
            options => options.OnFallback = @event =>
            {
                observed = @event.Outcome.Exception;
                return default;
            });

        var result = await shield.ExecuteAsync(_ => throw new InvalidOperationException("original"));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(observed!.Message).IsEqualTo("original");
    }

    [Test]
    public async Task FallbackTo_Is_Not_Invoked_On_Success()
    {
        var fired = false;
        var shield = Shield.For<int>().FallbackTo(
            42,
            options => options.OnFallback = _ =>
            {
                fired = true;
                return default;
            });

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(7));

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(fired).IsFalse();
    }

    [Test]
    public async Task FallbackTo_Respects_Ambient_Clause()
    {
        var shield = Shield.For<int>()
            .When<HttpRequestException>()
            .FallbackTo(42);

        await Assert.That(async () => await shield.ExecuteAsync(_ => throw new ArgumentException()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task FallbackTo_Works_With_Sync_And_Outcome_Execution()
    {
        var shield = Shield.For<int>().FallbackTo(42);

        var synchronous = shield.Execute(_ => throw new InvalidOperationException());
        var outcome = await shield.ExecuteOutcomeAsync(_ => throw new InvalidOperationException());

        await Assert.That(synchronous).IsEqualTo(42);
        await Assert.That(outcome.IsSuccess).IsTrue();
        await Assert.That(outcome.Result).IsEqualTo(42);
    }

    [Test]
    public async Task FallbackTo_Describes_Value_Fallback()
    {
        var shield = Shield.For<int>().FallbackTo(42);

        await Assert.That(shield.ToString()).IsEqualTo("Fallback(value)");
    }
}

namespace Kevlar.Tests;

public class FallbackTests
{
    [Test]
    public async Task Fallback_Value_Replaces_Exceptions()
    {
        var shield = Shield.For<string>().When<InvalidOperationException>().FallbackTo("fallback");

        var result = await shield.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(result).IsEqualTo("fallback");
    }

    [Test]
    public async Task Fallback_Factory_Receives_The_Handled_Outcome()
    {
        Exception? seen = null;
        var shield = Shield.For<string>()
            .When<InvalidOperationException>()
            .Fallback((outcome, _) =>
            {
                seen = outcome.Exception;
                return new ValueTask<string>("recovered");
            });

        var result = await shield.ExecuteAsync(_ => throw new InvalidOperationException("original"));

        await Assert.That(result).IsEqualTo("recovered");
        await Assert.That(seen!.Message).IsEqualTo("original");
    }

    [Test]
    public async Task Fallback_Applies_To_Handled_Results()
    {
        var shield = Shield.For<int>().WhenResultEquals(-1).FallbackTo(0);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Fallback_Is_Not_Used_On_Success()
    {
        var shield = Shield.For<int>().WhenResultEquals(-1).FallbackTo(0);

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Unhandled_Exceptions_Bypass_The_Fallback()
    {
        var shield = Shield.For<string>().When<InvalidOperationException>().FallbackTo("fallback");

        await Assert.That(async () => await shield.ExecuteAsync(_ => throw new ArgumentException()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task OnFallback_Fires()
    {
        var fired = false;
        var shield = Shield.For<int>()
            .WhenResultEquals(-1)
            .FallbackTo(0, options => options.OnFallback = _ =>
            {
                fired = true;
                return default;
            });

        await shield.ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(fired).IsTrue();
    }

    [Test]
    public async Task Retry_Then_Fallback_Composition()
    {
        var attempts = 0;
        var shield = Shield.For<string>()
            .When<InvalidOperationException>()
            .FallbackTo("gave up")
            .Retry(2, Backoff.None);

        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(result).IsEqualTo("gave up");
        await Assert.That(attempts).IsEqualTo(3);
    }
}

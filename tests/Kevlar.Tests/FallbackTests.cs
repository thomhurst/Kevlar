namespace Kevlar.Tests;

public class FallbackTests
{
    [Test]
    public async Task Fallback_Value_Replaces_Exceptions()
    {
        var policy = Policy.For<string>().Handle<InvalidOperationException>().Fallback("fallback");

        var result = await policy.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(result).IsEqualTo("fallback");
    }

    [Test]
    public async Task Fallback_Factory_Receives_The_Handled_Outcome()
    {
        Exception? seen = null;
        var policy = Policy.For<string>()
            .Handle<InvalidOperationException>()
            .Fallback((outcome, _) =>
            {
                seen = outcome.Exception;
                return new ValueTask<string>("recovered");
            });

        var result = await policy.ExecuteAsync(_ => throw new InvalidOperationException("original"));

        await Assert.That(result).IsEqualTo("recovered");
        await Assert.That(seen!.Message).IsEqualTo("original");
    }

    [Test]
    public async Task Fallback_Applies_To_Handled_Results()
    {
        var policy = Policy.For<int>().HandleResult(-1).Fallback(0);

        var result = await policy.ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Fallback_Is_Not_Used_On_Success()
    {
        var policy = Policy.For<int>().HandleResult(-1).Fallback(0);

        var result = await policy.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Unhandled_Exceptions_Bypass_The_Fallback()
    {
        var policy = Policy.For<string>().Handle<InvalidOperationException>().Fallback("fallback");

        await Assert.That(async () => await policy.ExecuteAsync(_ => throw new ArgumentException()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task OnFallback_Fires()
    {
        var fired = false;
        var policy = Policy.For<int>()
            .HandleResult(-1)
            .Fallback(0, onFallback: _ => fired = true);

        await policy.ExecuteAsync(_ => new ValueTask<int>(-1));

        await Assert.That(fired).IsTrue();
    }

    [Test]
    public async Task Retry_Then_Fallback_Composition()
    {
        var attempts = 0;
        var policy = Policy.For<string>()
            .Handle<InvalidOperationException>()
            .Fallback("gave up")
            .Retry(2, Backoff.None);

        var result = await policy.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(result).IsEqualTo("gave up");
        await Assert.That(attempts).IsEqualTo(3);
    }
}

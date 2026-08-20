namespace Kevlar.Tests;

/// <summary>
/// Guards the non-generic void fallback: it recovers void executions, receives the handled
/// exception, respects handling clauses, and refuses result-returning executions with a
/// descriptive error instead of inventing a default value.
/// </summary>
public class VoidFallbackTests
{
    [Test]
    public async Task A_Failing_Void_Execution_Is_Recovered()
    {
        var fallbackRan = false;
        var shield = Shield.When<InvalidOperationException>().Fallback((_, _) =>
        {
            fallbackRan = true;
            return default;
        });

        await shield.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(fallbackRan).IsTrue();
    }

    [Test]
    public async Task The_Fallback_Receives_The_Handled_Exception()
    {
        var original = new InvalidOperationException("original");
        Exception? seen = null;
        var shield = Shield.When<InvalidOperationException>().Fallback((exception, _) =>
        {
            seen = exception;
            return default;
        });

        await shield.ExecuteAsync(_ => throw original);

        await Assert.That(ReferenceEquals(seen, original)).IsTrue();
    }

    [Test]
    public async Task The_Exception_Free_Overload_Works_Too()
    {
        var fallbackRan = false;
        var shield = Shield.Timeout(TimeSpan.FromMinutes(1)).Fallback(_ =>
        {
            fallbackRan = true;
            return default;
        });

        await shield.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(fallbackRan).IsTrue();
    }

    [Test]
    public async Task Unhandled_Exception_Types_Pass_Through()
    {
        var fallbackRan = false;
        var shield = Shield.When<TimeoutExceededException>().Fallback((_, _) =>
        {
            fallbackRan = true;
            return default;
        });

        await Assert.That(async () => await shield.ExecuteAsync(_ => throw new InvalidOperationException()))
            .Throws<InvalidOperationException>();
        await Assert.That(fallbackRan).IsFalse();
    }

    [Test]
    public async Task A_Result_Returning_Execution_Is_Refused_With_Guidance()
    {
        var shield = Shield.When<InvalidOperationException>().Fallback((_, _) => default);

        InvalidOperationException? error = null;
        try
        {
            _ = await shield.ExecuteAsync<int>(_ => throw new InvalidOperationException());
        }
        catch (InvalidOperationException caught)
        {
            error = caught;
        }

        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("Shield.For<T>()");
    }

    [Test]
    public async Task Successful_Result_Executions_Are_Untouched()
    {
        var fallbackRan = false;
        var shield = Shield.When<InvalidOperationException>().Fallback((_, _) =>
        {
            fallbackRan = true;
            return default;
        });

        var result = await shield.ExecuteAsync(_ => new ValueTask<int>(42));

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(fallbackRan).IsFalse();
    }

    [Test]
    public async Task A_Throwing_Fallback_Surfaces_Its_Own_Exception()
    {
        var shield = Shield.When<InvalidOperationException>()
            .Fallback((_, _) => throw new ArgumentException("fallback failed"));

        await Assert.That(async () => await shield.ExecuteAsync(_ => throw new InvalidOperationException()))
            .Throws<ArgumentException>().WithMessage("fallback failed");
    }

    [Test]
    public async Task Void_Fallback_Works_Synchronously()
    {
        var fallbackRan = false;
        var shield = Shield.When<InvalidOperationException>().Fallback((_, _) =>
        {
            fallbackRan = true;
            return default;
        });

        shield.Execute(_ => throw new InvalidOperationException());

        await Assert.That(fallbackRan).IsTrue();
    }
}

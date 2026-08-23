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

    [Test]
    public async Task The_Static_Factory_Starts_A_Pipeline_With_A_Fallback()
    {
        Exception? seenException = null;
        var exceptionFree = false;
        var notified = 0;

        var withException = Shield.Fallback((exception, _) =>
        {
            seenException = exception;
            return default;
        });
        var withoutException = Shield.Fallback(_ =>
        {
            exceptionFree = true;
            return default;
        });
        var withExceptionAndOptions = Shield.Fallback(
            static (_, _) => default,
            options => options.OnFallback = _ => notified++);
        var withoutExceptionAndOptions = Shield.Fallback(
            static _ => default,
            options => options.OnFallback = _ => notified++);

        var original = new InvalidOperationException("boom");
        await withException.ExecuteAsync(_ => throw original);
        await withoutException.ExecuteAsync(_ => throw new InvalidOperationException());
        await withExceptionAndOptions.ExecuteAsync(_ => throw new InvalidOperationException());
        await withoutExceptionAndOptions.ExecuteAsync(_ => throw new InvalidOperationException());

        await Assert.That(ReferenceEquals(seenException, original)).IsTrue();
        await Assert.That(exceptionFree).IsTrue();
        await Assert.That(notified).IsEqualTo(2);
    }

    [Test]
    public async Task The_Static_Factory_Keeps_The_Fallback_Outermost()
    {
        var attempts = 0;
        var recovered = false;

        // Fallback first is the valid order: the retry runs inside it.
        var shield = Shield
            .Fallback((_, _) =>
            {
                recovered = true;
                return default;
            })
            .Retry(2, Backoff.None);

        await shield.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        await Assert.That(shield.ToString()).StartsWith("Fallback");
        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(recovered).IsTrue();
    }
}

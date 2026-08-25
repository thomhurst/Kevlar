namespace Kevlar.Tests;

/// <summary>
/// Guards the non-generic void fallback: it recovers void executions, receives the handled
/// exception, respects handling clauses, and refuses handled result-returning executions with a
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
    public async Task Fallback_Preserves_The_Shield_Static_Type()
    {
        Shield shield = Shield.Fallback(static _ => ValueTask.CompletedTask).Retry(0);

        await Assert.That(shield).IsNotNull();
    }

    [Test]
    public async Task A_Result_Returning_Execution_Is_Refused_With_Guidance()
    {
        var shield = Shield.When<InvalidOperationException>().Fallback(static (_, _) => default);

        InvalidOperationException? error = null;
        try
        {
            _ = await shield.ExecuteAsync<int>(static _ => throw new InvalidOperationException());
        }
        catch (InvalidOperationException caught)
        {
            error = caught;
        }

        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).Contains("Shield.For<T>()");
    }

    [Test]
    public async Task Successful_Result_Execution_Is_Refused_Without_Invoking_The_Action()
    {
        var actionRan = false;
        var fallbackRan = false;
        var shield = Shield.When<InvalidOperationException>().Fallback((_, _) =>
        {
            fallbackRan = true;
            return default;
        });

        await Assert.That(async () => await shield.ExecuteAsync(_ =>
            {
                actionRan = true;
                return new ValueTask<int>(42);
            }))
            .Throws<InvalidOperationException>();

        await Assert.That(actionRan).IsFalse();
        await Assert.That(fallbackRan).IsFalse();
    }

    [Test]
    public async Task Result_Execution_Is_Refused_Before_Outer_Retry_With_A_Different_Clause()
    {
        var attempts = 0;
        var fallbackRuns = 0;
        var shield = Shield
            .Retry(1, Backoff.None)
            .When<IOException>()
            .Fallback((_, _) =>
            {
                fallbackRuns++;
                return default;
            });

        await Assert.That(async () => await shield.ExecuteAsync<int>(_ =>
            {
                attempts++;
                throw new IOException();
            }))
            .Throws<InvalidOperationException>();

        await Assert.That(attempts).IsEqualTo(0);
        await Assert.That(fallbackRuns).IsEqualTo(0);
    }

    [Test]
    public async Task A_Void_Fallback_Cannot_Be_Lifted_Into_A_Typed_Shield()
    {
        var shield = Shield.Fallback(static _ => default);

        await Assert.That(() => shield.For<int>())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Shield.For<T>()");
    }

    [Test]
    public async Task A_Void_Fallback_Cannot_Be_Wrapped_Into_A_Typed_Shield()
    {
        var voidFallback = Shield.Fallback(static _ => default);
        var typed = Shield.For<int>();

        await Assert.That(() => typed.Wrap(voidFallback))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Shield.For<T>()");
        await Assert.That(() => voidFallback.Wrap(typed))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Shield.For<T>()");
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

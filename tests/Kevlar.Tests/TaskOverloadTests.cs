namespace Kevlar.Tests;

/// <summary>
/// Guards the <see cref="ShieldTaskExtensions"/> overloads: plain <see cref="Task"/>-returning
/// delegates must flow straight into a shield without manual <see cref="ValueTask"/> wrapping,
/// while async lambdas keep binding to the allocation-free instance overloads.
/// </summary>
public class TaskOverloadTests
{
    private static Task<int> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(7);

    private static Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [Test]
    public async Task Task_Returning_Method_Group_Executes()
    {
        var shield = Shield.Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(LoadAsync);

        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task Task_Returning_Lambda_Executes_And_Retries()
    {
        var attempts = 0;
        var shield = Shield.Retry(2, Backoff.None);

        var result = await shield.ExecuteAsync(ct =>
        {
            attempts++;
            return attempts < 3 ? Task.FromException<int>(new InvalidOperationException()) : Task.FromResult(42);
        });

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    public async Task Void_Task_Delegate_Executes_And_Retries()
    {
        var attempts = 0;
        var shield = Shield.Retry(1, Backoff.None);

        await shield.ExecuteAsync(ct =>
        {
            attempts++;
            return attempts < 2 ? Task.FromException(new InvalidOperationException()) : SaveAsync(ct);
        });

        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task Task_Overloads_Thread_State_Without_Closures()
    {
        var shield = Shield.Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(41, static (state, _) => Task.FromResult(state + 1));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Void_Task_Overload_Threads_State()
    {
        var seen = 0;
        var shield = Shield.Retry(0, Backoff.None);

        await shield.ExecuteAsync(41, (state, _) =>
        {
            seen = state + 1;
            return Task.CompletedTask;
        });

        await Assert.That(seen).IsEqualTo(42);
    }

    [Test]
    public async Task Task_Outcome_Execution_Captures_Failures()
    {
        var shield = Shield.Retry(0, Backoff.None);

        var outcome = await shield.ExecuteOutcomeAsync(_ => Task.FromException<int>(new InvalidOperationException("boom")));

        await Assert.That(outcome.IsSuccess).IsFalse();
        await Assert.That(outcome.Exception!.Message).IsEqualTo("boom");
    }

    [Test]
    public async Task Typed_Shield_Accepts_Task_Delegates()
    {
        var attempts = 0;
        var shield = Shield.For<int>().WhenResultEquals(0).Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(_ => Task.FromResult(attempts++ == 0 ? 0 : 5));

        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task Typed_Shield_Task_Overloads_Thread_State_And_Outcomes()
    {
        var shield = Shield.For<int>().WhenResultEquals(0).Retry(0, Backoff.None);

        var result = await shield.ExecuteAsync(20, static (state, _) => Task.FromResult(state + 22));
        await Assert.That(result).IsEqualTo(42);

        var outcome = await shield.ExecuteOutcomeAsync(_ => Task.FromResult(0));
        await Assert.That(outcome.IsSuccess).IsTrue();
        await Assert.That(outcome.Result).IsEqualTo(0);
    }

    [Test]
    public async Task Async_Lambdas_Still_Compile_Against_The_Instance_Overloads()
    {
        // An async lambda converts to both Task and ValueTask delegates; the instance
        // ValueTask overload must win so this stays allocation-free and unambiguous.
        var shield = Shield.Retry(1, Backoff.None);

        var result = await shield.ExecuteAsync(async ct =>
        {
            await Task.Yield();
            return 42;
        });

        await Assert.That(result).IsEqualTo(42);

        await shield.ExecuteAsync(async ct => await Task.Yield());
    }
}

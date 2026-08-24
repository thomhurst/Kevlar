namespace Kevlar.Tests;

public class ShieldEmptyTests
{
    [Test]
    public async Task Empty_Synchronous_Throw_Surfaces_As_Faulted_ValueTask()
    {
        var failure = new IOException("failure");

        var untyped = Shield.Empty.ExecuteAsync<int>(_ => throw failure);
        var typed = Shield<int>.Empty.ExecuteAsync(_ => throw failure);

        await Assert.That(untyped.IsFaulted).IsTrue();
        await Assert.That(typed.IsFaulted).IsTrue();
        await Assert.That(ReferenceEquals(await CaptureExceptionAsync(untyped), failure)).IsTrue();
        await Assert.That(ReferenceEquals(await CaptureExceptionAsync(typed), failure)).IsTrue();
    }

    [Test]
    public async Task Empty_State_And_Task_Overloads_Return_Faulted_ValueTasks()
    {
        var failure = new IOException("failure");

        var valueTaskState = Shield.Empty.ExecuteAsync<int, IOException>(
            failure,
            static (exception, _) => throw exception);
        var task = Shield.Empty.ExecuteAsync(
            (Func<CancellationToken, Task<int>>)(_ => throw failure));
        var taskState = Shield.Empty.ExecuteAsync(
            failure,
            (Func<IOException, CancellationToken, Task<int>>)(static (exception, _) => throw exception));

        await Assert.That(valueTaskState.IsFaulted).IsTrue();
        await Assert.That(task.IsFaulted).IsTrue();
        await Assert.That(taskState.IsFaulted).IsTrue();
        await Assert.That(ReferenceEquals(await CaptureExceptionAsync(valueTaskState), failure)).IsTrue();
        await Assert.That(ReferenceEquals(await CaptureExceptionAsync(task), failure)).IsTrue();
        await Assert.That(ReferenceEquals(await CaptureExceptionAsync(taskState), failure)).IsTrue();
    }

    [Test]
    public async Task Empty_And_Retry_Zero_Have_The_Same_Completion_Semantics()
    {
        await AssertParityAsync(
            Shield.Empty.ExecuteAsync(_ => new ValueTask<int>(42)),
            Shield.Retry(0, Backoff.None).ExecuteAsync(_ => new ValueTask<int>(42)));
        await AssertParityAsync(
            Shield.Empty.ExecuteAsync<int>(_ => throw new IOException("empty")),
            Shield.Retry(0, Backoff.None).ExecuteAsync<int>(_ => throw new IOException("retry")));

        var emptySuccess = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retrySuccess = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await AssertParityAsync(
            Shield.Empty.ExecuteAsync(_ => new ValueTask<int>(emptySuccess.Task)),
            Shield.Retry(0, Backoff.None).ExecuteAsync(_ => new ValueTask<int>(retrySuccess.Task)),
            () =>
            {
                emptySuccess.SetResult(42);
                retrySuccess.SetResult(42);
            });

        var emptyFailure = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryFailure = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await AssertParityAsync(
            Shield.Empty.ExecuteAsync(_ => new ValueTask<int>(emptyFailure.Task)),
            Shield.Retry(0, Backoff.None).ExecuteAsync(_ => new ValueTask<int>(retryFailure.Task)),
            () =>
            {
                emptyFailure.SetException(new IOException("empty"));
                retryFailure.SetException(new IOException("retry"));
            });

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await AssertParityAsync(
            Shield.Empty.ExecuteAsync(_ => new ValueTask<int>(42), cancellation.Token),
            Shield.Retry(0, Backoff.None).ExecuteAsync(_ => new ValueTask<int>(42), cancellation.Token));
    }

    [Test]
    public async Task Empty_Outcome_Captures_A_Synchronous_Throw()
    {
        var failure = new IOException("failure");

        var untyped = await Shield.Empty.ExecuteOutcomeAsync<int>(_ => throw failure);
        var typed = await Shield<int>.Empty.ExecuteOutcomeAsync(_ => throw failure);

        await Assert.That(ReferenceEquals(untyped.Exception, failure)).IsTrue();
        await Assert.That(ReferenceEquals(typed.Exception, failure)).IsTrue();
    }

    private static async Task<Exception> CaptureExceptionAsync<T>(ValueTask<T> execution)
    {
        try
        {
            _ = await execution;
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Execution did not fail.");
    }

    private static async Task AssertParityAsync(
        ValueTask<int> empty,
        ValueTask<int> retry,
        Action? release = null)
    {
        await Assert.That(empty.IsCompleted).IsEqualTo(retry.IsCompleted);
        await Assert.That(empty.IsFaulted).IsEqualTo(retry.IsFaulted);
        await Assert.That(empty.IsCanceled).IsEqualTo(retry.IsCanceled);

        release?.Invoke();

        var emptyCompletion = await CaptureCompletionAsync(empty);
        var retryCompletion = await CaptureCompletionAsync(retry);
        await Assert.That(emptyCompletion).IsEqualTo(retryCompletion);
    }

    private static async Task<Completion> CaptureCompletionAsync(ValueTask<int> execution)
    {
        try
        {
            return new Completion(await execution, null);
        }
        catch (Exception exception)
        {
            return new Completion(default, exception.GetType());
        }
    }

    private readonly record struct Completion(int Result, Type? ExceptionType);
}

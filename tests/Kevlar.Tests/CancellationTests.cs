namespace Kevlar.Tests;

public class CancellationTests
{
    [Test]
    public async Task An_Already_Cancelled_Token_Skips_The_Delegate()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;
        var shield = Shield.Retry(3, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync(_ =>
        {
            invoked = true;
            return new ValueTask<int>(1);
        }, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task An_Already_Cancelled_Token_Skips_The_Delegate_On_An_Empty_Policy()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;

        await Assert.That(async () => await Shield.Empty.ExecuteAsync(_ =>
        {
            invoked = true;
            return new ValueTask<int>(1);
        }, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task An_Already_Cancelled_Token_Skips_The_Delegate_Synchronously()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;
        var shield = Shield.Retry(3, Backoff.None);

        await Assert.That(() => shield.Execute(_ =>
        {
            invoked = true;
            return 1;
        }, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(invoked).IsFalse();
    }

    [Test]
    public async Task ExecuteOutcome_Captures_PreCancellation_Instead_Of_Throwing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var shield = Shield.Retry(3, Backoff.None);

        var outcome = await shield.ExecuteOutcomeAsync(_ => new ValueTask<int>(1), cancellation.Token);

        await Assert.That(outcome.IsSuccess).IsFalse();
        await Assert.That(outcome.Exception is OperationCanceledException).IsTrue();
    }

    [Test]
    public async Task The_Delegate_Receives_The_Callers_Token_When_No_Strategy_Replaces_It()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken seenToken = default;
        var shield = Shield.Retry(1, Backoff.None);

        await shield.ExecuteAsync(token =>
        {
            seenToken = token;
            return new ValueTask<int>(1);
        }, cancellation.Token);

        await Assert.That(seenToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task A_Timeout_Strategy_Hands_The_Delegate_A_Different_Token()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken seenToken = default;
        var shield = Shield.Timeout(TimeSpan.FromMinutes(10));

        await shield.ExecuteAsync(token =>
        {
            seenToken = token;
            return new ValueTask<int>(1);
        }, cancellation.Token);

        await Assert.That(seenToken == cancellation.Token).IsFalse();
        await Assert.That(seenToken.CanBeCanceled).IsTrue();
    }

    [Test]
    public async Task Cancelling_Mid_Execution_Suppresses_Retries()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.Retry(5, Backoff.None);

        await Assert.That(async () => await shield.ExecuteAsync<int>(async token =>
        {
            Interlocked.Increment(ref attempts);
            cancellation.Cancel();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return 1;
        }, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Explicitly_Handling_OperationCanceled_Enables_Retrying_It()
    {
        var attempts = 0;
        var shield = Shield.When<OperationCanceledException>().Retry(2, Backoff.None);

        // The delegate throws OperationCanceledException spontaneously (no token is cancelled),
        // and the explicit clause opts in to retrying it.
        var result = await shield.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new OperationCanceledException();
            }

            return new ValueTask<int>(attempts);
        });

        await Assert.That(result).IsEqualTo(3);
    }

    [Test]
    public async Task Cancellation_During_A_Void_Execution_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource();

        var task = Shield.Retry(3, Backoff.None).ExecuteAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
        }, cancellation.Token).AsTask();

        await started.Task;
        cancellation.Cancel();

        await Assert.That(async () => await task).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Sync_Cancellation_During_The_Delegate_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.Retry(5, Backoff.None);

        await Assert.That(() => shield.Execute<int>(token =>
        {
            attempts++;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return 1;
        }, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(attempts).IsEqualTo(1);
    }
}

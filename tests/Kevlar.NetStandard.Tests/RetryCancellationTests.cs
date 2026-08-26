namespace Kevlar.NetStandard.Tests;

public class RetryCancellationTests
{
    [Test]
    public async Task Zero_Delay_Hook_Cancellation_Stops_Next_Attempt()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.None;
            options.OnRetry = _ =>
            {
                cancellation.Cancel();
                return default;
            };
        });

        var outcome = await shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException("retry me");
        }, cancellation.Token);

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
    }

    [Test]
    public async Task Nonzero_Delay_Caller_Cancellation_Preserves_Caller_Token()
    {
        using var cancellation = new CancellationTokenSource();
        var retryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var shield = Shield.Retry(options =>
        {
            options.MaxRetries = 3;
            options.Backoff = Backoff.Constant(TimeSpan.FromDays(1));
            options.OnRetry = _ =>
            {
                retryStarted.TrySetResult();
                return default;
            };
        });

        var execution = shield.ExecuteOutcomeAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException("retry me");
        }, cancellation.Token).AsTask();
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
    }
}

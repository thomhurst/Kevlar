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
            options.OnRetry = _ => cancellation.Cancel();
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
}

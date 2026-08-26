using Kevlar.Chaos;

namespace Kevlar.NetStandard.Tests;

public class ChaosCancellationTests
{
    [Test]
    public async Task Latency_Caller_Cancellation_Preserves_Caller_Token_And_Skips_Action()
    {
        using var cancellation = new CancellationTokenSource();
        var injectionStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var actionCalls = 0;
        var shield = ChaosShield.Latency(options =>
        {
            options.Enabled = true;
            options.Delay = TimeSpan.FromDays(1);
            options.OnInjected = _ =>
            {
                injectionStarted.TrySetResult();
                return default;
            };
        });

        var execution = shield.ExecuteOutcomeAsync<int>(_ =>
        {
            actionCalls++;
            return new ValueTask<int>(42);
        }, cancellation.Token).AsTask();
        await injectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var outcome = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(outcome.Exception).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)outcome.Exception!).CancellationToken)
            .IsEqualTo(cancellation.Token);
        await Assert.That(actionCalls).IsEqualTo(0);
    }
}

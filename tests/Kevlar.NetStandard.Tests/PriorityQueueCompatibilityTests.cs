namespace Kevlar.NetStandard.Tests;

public class PriorityQueueCompatibilityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Priority_Eviction_And_Cancellation_Work_On_The_NetStandard_Asset(bool rateLimit)
    {
        var shield = rateLimit ? Shield.RateLimit(options =>
        {
            options.Permits = 1;
            options.Window = TimeSpan.FromDays(1);
            options.QueueLimit = 1;
            options.UsePriorityQueue = true;
        }) : Shield.ConcurrencyLimit(options =>
        {
            options.MaxConcurrency = 1;
            options.QueueLimit = 1;
            options.UsePriorityQueue = true;
        });
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        var active = shield.ExecuteAsync(_ => new ValueTask<int>(release.Task)).AsTask();
        var low = shield.ExecuteAsync(static _ => new ValueTask<int>(2)).AsTask();
        var high = shield.ExecuteWithContextAsync(1,
            static (priority, properties) => properties.Set(KevlarKeys.Priority, priority),
            static (_, _) => new ValueTask<int>(3), caller.Token).AsTask();
        try
        {
            await Assert.That(async () => await low.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<ExecutionRejectedException>();
            caller.Cancel();
            await Assert.That(async () => await high.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<OperationCanceledException>();
        }
        finally
        {
            caller.Cancel();
            release.TrySetResult(1);
            await active.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}

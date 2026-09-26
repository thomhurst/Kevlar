namespace Kevlar.Tests;

public class TimeoutPoolTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Concurrent_Rentals_Isolate_Cancellation_Across_Reused_Sources(bool yield)
    {
        var shield = Shield.Timeout(TimeSpan.FromSeconds(30));
        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var previous = new CancellationTokenSource();
            await shield.ExecuteAsync(static token =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }, previous.Token);

            for (var iteration = 0; iteration < 100; iteration++)
            {
                using var current = new CancellationTokenSource();
                var cancelCurrent = iteration % 2 == 0;
                var result = await shield.ExecuteOutcomeAsync<int>(async token =>
                {
                    if (yield)
                    {
                        await Task.Yield();
                    }
                    // A returned source must no longer be linked to its previous caller.
                    previous.Cancel();
                    token.ThrowIfCancellationRequested();
                    if (cancelCurrent)
                    {
                        current.Cancel();
                        token.ThrowIfCancellationRequested();
                    }
                    return 42;
                }, current.Token);

                if (cancelCurrent)
                {
                    await Assert.That(result.Exception).IsTypeOf<OperationCanceledException>();
                    await Assert.That(((OperationCanceledException)result.Exception!).CancellationToken)
                        .IsEqualTo(current.Token);
                }
                else
                {
                    await Assert.That(result.IsSuccess).IsTrue();
                    await Assert.That(result.Result).IsEqualTo(42);
                }
            }
        }));
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(20));
    }
}

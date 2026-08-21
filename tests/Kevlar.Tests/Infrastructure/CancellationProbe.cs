namespace Kevlar.Tests;

internal sealed class CancellationProbe : IDisposable
{
    private readonly TaskCompletionSource _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _registration;
    private int _count;

    public CancellationProbe(CancellationToken cancellationToken)
    {
        _registration = cancellationToken.Register(static state => ((CancellationProbe)state!).OnCancellation(), this);
    }

    public int Count => Volatile.Read(ref _count);

    public Task WaitAsync() => TestHelpers.WaitAsync(_observed.Task, "cancellation registration to run");

    public void Dispose() => _registration.Dispose();

    private void OnCancellation()
    {
        Interlocked.Increment(ref _count);
        _observed.TrySetResult();
    }
}

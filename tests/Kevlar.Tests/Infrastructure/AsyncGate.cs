namespace Kevlar.Tests;

internal sealed class AsyncGate(string name)
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsEntered => _entered.Task.IsCompleted;

    public bool IsReleased => _released.Task.IsCompleted;

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        _entered.TrySetResult();
        var release = cancellationToken.CanBeCanceled
            ? _released.Task.WaitAsync(cancellationToken)
            : _released.Task;
        await TestHelpers.WaitAsync(release, $"gate '{name}' to be released");
    }

    public Task WaitForEntryAsync() => TestHelpers.WaitAsync(_entered.Task, $"gate '{name}' to be entered");

    public void Release() => _released.TrySetResult();
}

namespace Kevlar.Tests;

internal sealed class AsyncBarrier
{
    private readonly object _sync = new();
    private readonly string _name;
    private readonly int _participantCount;
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public AsyncBarrier(string name, int participantCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(participantCount);
        _name = name;
        _participantCount = participantCount;
    }

    public int ArrivedCount
    {
        get
        {
            lock (_sync)
            {
                return _arrived;
            }
        }
    }

    public async Task SignalAndWaitAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_released.Task.IsCompleted)
            {
                return;
            }

            _arrived++;
            if (_arrived > _participantCount)
            {
                throw new InvalidOperationException(
                    $"Barrier '{_name}' received more than {_participantCount} participants before release.");
            }

            if (_arrived == _participantCount)
            {
                _allArrived.TrySetResult();
            }
        }

        var release = cancellationToken.CanBeCanceled
            ? _released.Task.WaitAsync(cancellationToken)
            : _released.Task;
        await TestHelpers.WaitAsync(release, $"barrier '{_name}' to be released");
    }

    public Task WaitForAllAsync() =>
        TestHelpers.WaitAsync(_allArrived.Task, $"all {_participantCount} participants at barrier '{_name}'");

    public void Release() => _released.TrySetResult();
}

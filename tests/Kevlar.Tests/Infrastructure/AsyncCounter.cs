namespace Kevlar.Tests;

internal sealed class AsyncCounter(string name)
{
    private readonly object _sync = new();
    private readonly List<Waiter> _waiters = [];
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public int Signal()
    {
        List<Waiter>? completed = null;
        int count;

        lock (_sync)
        {
            count = ++_count;
            for (var index = _waiters.Count - 1; index >= 0; index--)
            {
                if (_waiters[index].Target > count)
                {
                    continue;
                }

                completed ??= [];
                completed.Add(_waiters[index]);
                _waiters.RemoveAt(index);
            }
        }

        if (completed is not null)
        {
            foreach (var waiter in completed)
            {
                waiter.Completion.TrySetResult(count);
            }
        }

        return count;
    }

    public async Task<int> WaitForAsync(int target)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target);
        Waiter waiter;

        lock (_sync)
        {
            if (_count >= target)
            {
                return _count;
            }

            waiter = new Waiter(target);
            _waiters.Add(waiter);
        }

        try
        {
            return await TestHelpers.WaitAsync(waiter.Completion.Task, $"counter '{name}' to reach {target}");
        }
        finally
        {
            lock (_sync)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    private sealed record Waiter(int Target)
    {
        public TaskCompletionSource<int> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

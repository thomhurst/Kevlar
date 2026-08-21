namespace Kevlar.Testing;

/// <summary>Counts delegate attempts and cancellation requests without changing shield behavior.</summary>
public sealed class ExecutionProbe
{
    private readonly object _gate = new();
    private TaskCompletionSource<bool> _changed = CreateSignal();
    private long _state;

    /// <summary>Gets the number of delegate invocations observed so far.</summary>
    public int AttemptCount => CaptureCounts().Attempts;

    /// <summary>Gets the number of active attempt tokens that requested cancellation.</summary>
    public int CancellationCount => CaptureCounts().Cancellations;

    /// <summary>Wraps an untyped asynchronous execution delegate.</summary>
    public Func<CancellationToken, ValueTask> Wrap(Func<CancellationToken, ValueTask> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        return token => InvokeAsync(action, token);
    }

    /// <summary>Wraps a typed asynchronous execution delegate.</summary>
    public Func<CancellationToken, ValueTask<TResult>> Wrap<TResult>(
        Func<CancellationToken, ValueTask<TResult>> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        return token => InvokeAsync(action, token);
    }

    /// <summary>Gets an immutable snapshot of current counts.</summary>
    public ExecutionProbeSnapshot GetSnapshot()
    {
        var counts = CaptureCounts();
        return new ExecutionProbeSnapshot(counts.Attempts, counts.Cancellations);
    }

    /// <summary>Waits until at least <paramref name="count"/> attempts have started.</summary>
    public Task WaitForAttemptCountAsync(int count, CancellationToken cancellationToken = default) =>
        WaitForCountAsync(static probe => probe.AttemptCount, count, cancellationToken);

    /// <summary>Waits until at least <paramref name="count"/> active attempts observed cancellation.</summary>
    public Task WaitForCancellationCountAsync(int count, CancellationToken cancellationToken = default) =>
        WaitForCountAsync(static probe => probe.CancellationCount, count, cancellationToken);

    private async ValueTask InvokeAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        RecordAttempt();
        using var registration = cancellationToken.Register(
            static state => ((ExecutionProbe)state!).RecordCancellation(), this);
        await action(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<TResult> InvokeAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken)
    {
        RecordAttempt();
        using var registration = cancellationToken.Register(
            static state => ((ExecutionProbe)state!).RecordCancellation(), this);
        return await action(cancellationToken).ConfigureAwait(false);
    }

    private void RecordAttempt()
    {
        Interlocked.Add(ref _state, 1L << 32);
        SignalChanged();
    }

    private void RecordCancellation()
    {
        Interlocked.Increment(ref _state);
        SignalChanged();
    }

    private void SignalChanged()
    {
        TaskCompletionSource<bool> signal;
        lock (_gate)
        {
            signal = _changed;
            _changed = CreateSignal();
        }

        signal.TrySetResult(true);
    }

    private async Task WaitForCountAsync(
        Func<ExecutionProbe, int> getCount,
        int count,
        CancellationToken cancellationToken)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        while (getCount(this) < count)
        {
            Task signal;
            lock (_gate)
            {
                if (getCount(this) >= count)
                {
                    return;
                }

                signal = _changed.Task;
            }

            await WaitWithCancellationAsync(signal, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellation);
        if (ReferenceEquals(
            await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false),
            cancellation.Task))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        await task.ConfigureAwait(false);
    }

    private static TaskCompletionSource<bool> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private (int Attempts, int Cancellations) CaptureCounts()
    {
        var state = Volatile.Read(ref _state);
        return ((int)(state >> 32), (int)(state & uint.MaxValue));
    }
}

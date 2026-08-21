namespace Kevlar.Tests;

internal sealed class GatedDelegate<T>
{
    private readonly AsyncCounter _invocations;
    private readonly AsyncGate _gate;
    private readonly Func<int, CancellationToken, ValueTask<T>> _completion;

    public GatedDelegate(string name, Func<int, CancellationToken, ValueTask<T>> completion)
    {
        _invocations = new AsyncCounter($"{name} invocations");
        _gate = new AsyncGate(name);
        _completion = completion;
    }

    public async ValueTask<T> InvokeAsync(CancellationToken cancellationToken)
    {
        var invocation = _invocations.Signal();
        await _gate.EnterAsync();
        return await _completion(invocation, cancellationToken);
    }

    public Task<int> WaitForInvocationsAsync(int count) => _invocations.WaitForAsync(count);

    public void Release() => _gate.Release();
}

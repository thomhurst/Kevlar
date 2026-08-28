namespace Kevlar.Internal;

internal sealed class ExecutionReentrancyGuard
{
    private readonly AsyncLocal<Scope?> _current = new();

    public bool Active => _current.Value is { Active: true };

    public Scope Enter()
    {
        var scope = new Scope(_current.Value);
        _current.Value = scope;
        return scope;
    }

    public void Restore(Scope scope) => _current.Value = scope.Parent;

    internal sealed class Scope(Scope? parent)
    {
        private int _active = 1;

        public Scope? Parent { get; } = parent;

        public bool Active => Volatile.Read(ref _active) != 0
            || Parent is { Active: true };

        public void Deactivate() => Volatile.Write(ref _active, 0);
    }
}

using System.Threading;

namespace Kevlar.Chaos;

/// <summary>
/// Carries optional operation and environment labels used to bound chaos injection.
/// </summary>
public static class ChaosScope
{
    private static readonly AsyncLocal<State?> Current = new();

    /// <summary>Gets the current operation label, if any.</summary>
    public static string? Operation => Current.Value?.Operation;

    /// <summary>Gets the current environment label, if any.</summary>
    public static string? Environment => Current.Value?.Environment;

    /// <summary>
    /// Begins an asynchronous-flow scope. Omitted values inherit from the enclosing scope.
    /// </summary>
    /// <param name="operation">The operation label.</param>
    /// <param name="environment">The environment label.</param>
    /// <returns>A handle that restores the enclosing scope when disposed.</returns>
    public static IDisposable Begin(string? operation = null, string? environment = null)
    {
        var prior = Current.Value;
        Current.Value = new State(operation ?? prior?.Operation, environment ?? prior?.Environment);
        return new ScopeHandle(prior);
    }

    internal static void Capture(out string? operation, out string? environment)
    {
        var current = Current.Value;
        operation = current?.Operation;
        environment = current?.Environment;
    }

    private sealed class State
    {
        public State(string? operation, string? environment)
        {
            Operation = operation;
            Environment = environment;
        }

        public string? Operation { get; }

        public string? Environment { get; }
    }

    private sealed class ScopeHandle : IDisposable
    {
        private State? _prior;
        private bool _disposed;

        public ScopeHandle(State? prior) => _prior = prior;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Current.Value = _prior;
            _prior = null;
            _disposed = true;
        }
    }
}

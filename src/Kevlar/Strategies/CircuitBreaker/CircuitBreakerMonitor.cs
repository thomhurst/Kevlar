using Kevlar.Strategies;

namespace Kevlar;

/// <summary>
/// Observes and manually controls one or more circuit breakers. Create one, assign it to
/// each <see cref="CircuitBreakerOptions.Monitor"/> to control, and keep a reference to it.
/// </summary>
public sealed class CircuitBreakerMonitor
{
    private CircuitBreakerCore[]? _cores;

    /// <summary>
    /// The worst state among the bound circuits, ordered isolated, open, half-open, then closed.
    /// </summary>
    public CircuitState State
    {
        get
        {
            var cores = BoundCores();
            var state = cores[0].State;
            var severity = Severity(state);
            for (var index = 1; index < cores.Length; index++)
            {
                var candidate = cores[index].State;
                var candidateSeverity = Severity(candidate);
                if (candidateSeverity > severity)
                {
                    state = candidate;
                    severity = candidateSeverity;
                }
            }

            return state;
        }
    }

    /// <summary>
    /// Raised on every state transition of each bound circuit, after
    /// <see cref="CircuitBreakerOptions.OnStateChanged"/> completes. Transitions are delivered
    /// serially per circuit outside its lock; different circuits may publish concurrently.
    /// Handlers may read state or call <see cref="Reset"/> or <see cref="Isolate"/> without
    /// deadlocking. Handlers run synchronously and block later transitions from the same circuit,
    /// so they should not perform I/O, wait on external work, or otherwise run for a long time.
    /// </summary>
    public event Action<CircuitBreakerStateChangedEvent>? StateChanged;

    /// <summary>
    /// Forces every bound circuit open. Executions are rejected until <see cref="Reset"/> is called.
    /// A non-reentrant caller blocks until transition observers complete; use
    /// <see cref="IsolateAsync"/> when an <see cref="CircuitBreakerOptions.OnStateChanged"/>
    /// callback may yield. A call made from a transition observer queues its transition and returns
    /// before that queued transition reaches observers.
    /// </summary>
    public void Isolate()
    {
        foreach (var core in BoundCores())
        {
            core.Isolate();
        }
    }

    /// <summary>
    /// Forces every bound circuit open and asynchronously waits for configured transition observers.
    /// A reentrant call from a transition callback queues its transition behind the active
    /// publication and returns before that queued transition reaches observers; do not use the
    /// returned task to order reentrant transition work.
    /// Executions are rejected until <see cref="ResetAsync"/> is called.
    /// </summary>
    public ValueTask IsolateAsync()
    {
        var cores = BoundCores();
        return cores.Length == 1
            ? cores[0].IsolateAsync()
            : IsolateAllAsync(cores);
    }

    /// <summary>
    /// Closes every bound circuit and clears all failure metrics. A non-reentrant caller blocks
    /// until transition observers complete; use <see cref="ResetAsync"/> when an
    /// <see cref="CircuitBreakerOptions.OnStateChanged"/> callback may yield. A call made from a
    /// transition observer queues its transition and returns before that queued transition reaches
    /// observers.
    /// </summary>
    public void Reset()
    {
        foreach (var core in BoundCores())
        {
            core.Reset();
        }
    }

    /// <summary>
    /// Closes every bound circuit, clears all failure metrics, and asynchronously waits for configured
    /// transition observers. A reentrant call from a transition callback queues its transition
    /// behind the active publication and returns before that queued transition reaches observers;
    /// do not use the returned task to order reentrant transition work.
    /// </summary>
    public ValueTask ResetAsync()
    {
        var cores = BoundCores();
        return cores.Length == 1
            ? cores[0].ResetAsync()
            : ResetAllAsync(cores);
    }

    internal void Bind(CircuitBreakerCore core)
    {
        while (true)
        {
            var cores = Volatile.Read(ref _cores);
            var updated = new CircuitBreakerCore[(cores?.Length ?? 0) + 1];
            if (cores is not null)
            {
                Array.Copy(cores, updated, cores.Length);
            }

            updated[^1] = core;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _cores, updated, cores), cores))
            {
                return;
            }
        }
    }

    internal void Raise(in CircuitBreakerStateChangedEvent stateChangedEvent)
    {
        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<CircuitBreakerStateChangedEvent> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(stateChangedEvent);
            }
            catch (Exception exception)
            {
                KevlarDiagnostics.ReportCallbackError(
                    CallbackErrorKind.CircuitMonitor,
                    stateChangedEvent.Context,
                    exception,
                    "CircuitBreakerMonitor.OnStateChanged");
            }
        }
    }

    private CircuitBreakerCore[] BoundCores() =>
        Volatile.Read(ref _cores) ?? throw new InvalidOperationException(
            "This CircuitBreakerMonitor has not been bound. Assign it to CircuitBreakerOptions.Monitor when building the shield.");

    private static int Severity(CircuitState state) => state switch
    {
        CircuitState.Closed => 0,
        CircuitState.HalfOpen => 1,
        CircuitState.Open => 2,
        CircuitState.Isolated => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static async ValueTask IsolateAllAsync(CircuitBreakerCore[] cores)
    {
        foreach (var core in cores)
        {
            await core.IsolateAsync().ConfigureAwait(false);
        }
    }

    private static async ValueTask ResetAllAsync(CircuitBreakerCore[] cores)
    {
        foreach (var core in cores)
        {
            await core.ResetAsync().ConfigureAwait(false);
        }
    }
}

# Deterministic asynchronous tests

Unit tests in `Kevlar.Tests` must use explicit signals, gates, barriers, cancellation registrations, or a controlled `TimeProvider`. Do not use finite `Task.Delay`, polling loops, `Thread.Sleep`, or elapsed-time tolerances to make concurrent work “probably” reach a state. Integration tests may exercise real I/O timing when elapsed behavior is the contract.

Helpers in `Kevlar.Tests/Infrastructure` provide the shared vocabulary:

- `GatedDelegate<T>` and `AsyncGate` expose entry and explicit release.
- `AsyncBarrier` holds a named number of participants until the test releases them.
- `AsyncCounter` waits for a named event count without polling.
- `CancellationProbe` observes cancellation-registration execution.
- `ControlledTimeProvider` records timers, fires them explicitly, and captures callbacks that must outlive timer disposal.
- `RaceRunner` executes both named orderings with a reproducible seed and includes the name, iteration, seed, and ordering in failures.

Every helper wait has a five-second watchdog. The watchdog is only a deadlock bound, never the mechanism that creates the state under test. Prefer `FakeTimeProvider` when only clock advancement matters. Keep real time only for the synchronous timeout smoke test, where blocking behavior itself is the contract.

An infinite `Task.Delay` tied to the execution token is also valid when “remain pending until cancellation” is the behavior under test; it does not use elapsed time to coordinate assertions.

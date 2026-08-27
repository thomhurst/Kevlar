# Glossary

Kevlar uses a small vocabulary consistently across strategies, diagnostics, and documentation.
These definitions are conceptual; links point to the API or guide where the behavior becomes
concrete.

## Ambient handling

The exception and result predicates currently inherited by strategies added to a fluent chain.
Calling `When...` starts an ambient handling clause; `Or...` extends it. Each reactive strategy
captures the clause that exists when that strategy is added. See [Handling failures](handling-failures.md).

## Proactive strategy

A strategy that acts before an operation produces an outcome. Timeouts, rate limits, concurrency
limits, and hedging are proactive: they budget, admit, or schedule work. They do not need an
exception or result predicate to decide whether to start.

## Reactive strategy

A strategy that reacts after an attempt produces an exception or handled result. Retry, circuit
breaker, and fallback are reactive. Their handling clause defines which outcomes affect them;
unhandled outcomes pass through unchanged.

## Outcome

A success value or an exception represented without throwing between pipeline layers. Public
`Outcome` and `Outcome<T>` APIs support no-throw execution; ordinary `Execute...` methods throw the
terminal exception only at the caller boundary.

## Strategy

One resilience behavior in a shield. A fluent chain builds an immutable linked sequence of
strategies. The first strategy added is outermost, so it observes everything performed by the
strategies added after it.

## Shield

An immutable, thread-safe resilience pipeline. `Shield` protects operations without results;
`Shield<T>` can also judge results. A built shield may be safely shared when callers should share
breaker, limiter, or retry-related state.

## Judge

To evaluate an exception or result against a handling predicate. Benchmark descriptions use
“judging” for this predicate cost; it does not imply throwing or changing the outcome.

## Seal

To end inheritance of an ambient handling clause across a composition boundary. `Wrap` and
`Compose` seal the wrapped pipeline: strategies appended afterward return to default handling until
another `When...` clause is declared. Sealing prevents a caller's clause from silently changing a
library-owned pipeline.

## Attempt

One invocation of the protected delegate. Retries and hedges create additional attempts. Retry
counts use additional-attempt semantics: `Retry(3)` permits the original plus three retries. Event
models document their own attempt-number convention.

## Terminal outcome

The value or exception selected after every strategy finishes. Superseded retry results and losing
hedge results are pipeline-owned and disposed; the selected terminal result remains caller-owned.

## Operation key

A bounded diagnostic label carried by `KevlarContext`. It identifies an operation within a named
shield without changing strategy state. Avoid unbounded values such as request IDs.

## Snapshot

An immutable view captured at a defined boundary. Options and routing collections are snapshotted
when documented, and testing snapshots copy live state so later executions cannot mutate an
assertion already obtained.

## Partition

Independent strategy state selected by a key through [`PartitionedShield`](partitioning.md). Partitions isolate
tenants or endpoints while sharing one pipeline definition; capacity and eviction keep retained
state bounded. See [Partitioning](partitioning.md).

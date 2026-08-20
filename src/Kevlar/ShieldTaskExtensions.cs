using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// <see cref="Task"/>-based execution overloads. Most application code returns
/// <see cref="Task{T}"/> rather than <see cref="ValueTask{T}"/>; these overloads let such
/// delegates flow straight into a shield: <c>shield.ExecuteAsync(ct =&gt; LoadUserAsync(id, ct))</c>.
/// </summary>
/// <remarks>
/// They are extension methods deliberately: the instance <c>ValueTask</c> overloads always win
/// overload resolution when both apply (async lambdas), so these only kick in for delegates that
/// genuinely return <see cref="Task"/> — with no ambiguity and no behavior change elsewhere.
/// </remarks>
public static class ShieldTaskExtensions
{
    // ── Shield ──────────────────────────────────────────────────────────────────────────

    /// <summary>Executes the <see cref="Task{T}"/>-returning delegate through the pipeline.</summary>
    public static ValueTask<T> ExecuteAsync<T>(this Shield shield, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteAsync(action, static (a, token) => new ValueTask<T>(a(token)), cancellationToken);
    }

    /// <summary>Executes the <see cref="Task{T}"/>-returning delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public static ValueTask<T> ExecuteAsync<T, TState>(this Shield shield, TState state, Func<TState, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteAsync((state, action), static (s, token) => new ValueTask<T>(s.action(s.state, token)), cancellationToken);
    }

    /// <summary>Executes the <see cref="Task"/>-returning void delegate through the pipeline.</summary>
    public static ValueTask ExecuteAsync(this Shield shield, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteAsync(action, static (a, token) => new ValueTask(a(token)), cancellationToken);
    }

    /// <summary>Executes the <see cref="Task"/>-returning void delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public static ValueTask ExecuteAsync<TState>(this Shield shield, TState state, Func<TState, CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteAsync((state, action), static (s, token) => new ValueTask(s.action(s.state, token)), cancellationToken);
    }

    /// <summary>Executes the <see cref="Task{T}"/>-returning delegate through the pipeline and returns the outcome instead of throwing.</summary>
    public static ValueTask<Outcome<T>> ExecuteOutcomeAsync<T>(this Shield shield, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteOutcomeAsync(token => new ValueTask<T>(action(token)), cancellationToken);
    }

    // ── Shield<TResult> ─────────────────────────────────────────────────────────────────

    /// <summary>Executes the <see cref="Task{TResult}"/>-returning delegate through the pipeline.</summary>
    public static ValueTask<TResult> ExecuteAsync<TResult>(this Shield<TResult> shield, Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteAsync(action, static (a, token) => new ValueTask<TResult>(a(token)), cancellationToken);
    }

    /// <summary>Executes the <see cref="Task{TResult}"/>-returning delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public static ValueTask<TResult> ExecuteAsync<TResult, TState>(this Shield<TResult> shield, TState state, Func<TState, CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteAsync((state, action), static (s, token) => new ValueTask<TResult>(s.action(s.state, token)), cancellationToken);
    }

    /// <summary>Executes the <see cref="Task{TResult}"/>-returning delegate through the pipeline and returns the outcome instead of throwing.</summary>
    public static ValueTask<Outcome<TResult>> ExecuteOutcomeAsync<TResult>(this Shield<TResult> shield, Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteOutcomeAsync(token => new ValueTask<TResult>(action(token)), cancellationToken);
    }
}

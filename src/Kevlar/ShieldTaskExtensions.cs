using Kevlar.Internal;

namespace Kevlar;

#pragma warning disable RS0026, RS0027 // Task/ValueTask overload parity intentionally keeps CancellationToken optional.

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

    /// <summary>Executes a context-aware <see cref="Task{TResult}"/> delegate using state inherited from <paramref name="parentContext"/>.</summary>
    public static ValueTask<T> ExecuteWithContextAsync<T>(
        this Shield shield,
        Func<KevlarContext, Task<T>> action,
        KevlarContext parentContext)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        return shield.ExecuteWithContextAsync(action, static (a, context) => new ValueTask<T>(a(context)), parentContext);
    }

    /// <summary>Executes a context-aware <see cref="Task{TResult}"/> delegate using state inherited from <paramref name="parentContext"/>, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public static ValueTask<T> ExecuteWithContextAsync<T, TState>(
        this Shield shield,
        TState state,
        Func<TState, KevlarContext, Task<T>> action,
        KevlarContext parentContext)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        return shield.ExecuteWithContextAsync(
            (state, action),
            static (s, context) => new ValueTask<T>(s.action(s.state, context)),
            parentContext);
    }

    /// <summary>Initializes execution properties, then executes a context-aware <see cref="Task{TResult}"/>-returning delegate.</summary>
    public static ValueTask<T> ExecuteWithContextAsync<T, TState>(
        this Shield shield,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteWithContextAsync(
            (state, initializeProperties, action),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static (s, context) => new ValueTask<T>(s.action(s.state, context)),
            cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, executes a context-aware <see cref="Task{TResult}"/> delegate,
    /// then exposes final properties before the pooled context is returned.
    /// </summary>
    public static ValueTask<T> ExecuteWithContextAsync<T, TState>(
        this Shield shield,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, Task<T>> action,
        Action<TState, KevlarProperties> onCompleted,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(onCompleted, nameof(onCompleted));
        return shield.ExecuteWithContextAsync(
            (state, initializeProperties, action, onCompleted),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static (s, context) => new ValueTask<T>(s.action(s.state, context)),
            static (s, properties) => s.onCompleted(s.state, properties),
            cancellationToken);
    }

    /// <summary>
    /// Executes a context-aware <see cref="Task{TResult}"/>-returning delegate without seeding
    /// execution properties. The context is pooled; never retain it beyond the delegate.
    /// </summary>
    public static ValueTask<T> ExecuteWithContextAsync<T>(
        this Shield shield,
        Func<KevlarContext, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteWithContextAsync(
            action,
            static (_, _) => { },
            static (a, context) => new ValueTask<T>(a(context)),
            cancellationToken);
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

    /// <summary>Executes a context-aware <see cref="Task"/> delegate using state inherited from <paramref name="parentContext"/>.</summary>
    public static ValueTask ExecuteWithContextAsync(
        this Shield shield,
        Func<KevlarContext, Task> action,
        KevlarContext parentContext)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        return shield.ExecuteWithContextAsync(action, static (a, context) => new ValueTask(a(context)), parentContext);
    }

    /// <summary>Executes a context-aware <see cref="Task"/> delegate using state inherited from <paramref name="parentContext"/>, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public static ValueTask ExecuteWithContextAsync<TState>(
        this Shield shield,
        TState state,
        Func<TState, KevlarContext, Task> action,
        KevlarContext parentContext)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        return shield.ExecuteWithContextAsync(
            (state, action),
            static (s, context) => new ValueTask(s.action(s.state, context)),
            parentContext);
    }

    /// <summary>Initializes execution properties, then executes a context-aware <see cref="Task"/>-returning void delegate.</summary>
    public static ValueTask ExecuteWithContextAsync<TState>(
        this Shield shield,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, Task> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteWithContextAsync(
            (state, initializeProperties, action),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static (s, context) => new ValueTask(s.action(s.state, context)),
            cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, executes a context-aware <see cref="Task"/> delegate,
    /// then exposes final properties before the pooled context is returned.
    /// </summary>
    public static ValueTask ExecuteWithContextAsync<TState>(
        this Shield shield,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, Task> action,
        Action<TState, KevlarProperties> onCompleted,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(onCompleted, nameof(onCompleted));
        return shield.ExecuteWithContextAsync(
            (state, initializeProperties, action, onCompleted),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static (s, context) => new ValueTask(s.action(s.state, context)),
            static (s, properties) => s.onCompleted(s.state, properties),
            cancellationToken);
    }

    /// <summary>
    /// Executes a context-aware <see cref="Task"/>-returning void delegate without seeding execution
    /// properties. The context is pooled; never retain it beyond the delegate.
    /// </summary>
    public static ValueTask ExecuteWithContextAsync(
        this Shield shield,
        Func<KevlarContext, Task> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteWithContextAsync(
            action,
            static (_, _) => { },
            static (a, context) => new ValueTask(a(context)),
            cancellationToken);
    }

    /// <summary>Executes the <see cref="Task{T}"/>-returning delegate through the pipeline and returns the outcome instead of throwing.</summary>
    public static ValueTask<Outcome<T>> ExecuteOutcomeAsync<T>(this Shield shield, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteOutcomeAsync(token => new ValueTask<T>(action(token)), cancellationToken);
    }

    /// <summary>Executes the <see cref="Task{T}"/>-returning delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations, and returns the outcome instead of throwing.</summary>
    public static ValueTask<Outcome<T>> ExecuteOutcomeAsync<T, TState>(this Shield shield, TState state, Func<TState, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteOutcomeAsync((state, action), static (s, token) => new ValueTask<T>(s.action(s.state, token)), cancellationToken);
    }

    /// <summary>Executes a <see cref="Task"/>-returning void delegate and returns its outcome.</summary>
    public static ValueTask<Outcome> ExecuteOutcomeAsync(
        this Shield shield,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteOutcomeAsync(token => new ValueTask(action(token)), cancellationToken);
    }

    /// <summary>
    /// Executes a <see cref="Task"/>-returning void delegate, threading <paramref name="state"/> to
    /// avoid closure allocations, and returns its outcome.
    /// </summary>
    public static ValueTask<Outcome> ExecuteOutcomeAsync<TState>(
        this Shield shield,
        TState state,
        Func<TState, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteOutcomeAsync(
            (state, action),
            static (s, token) => new ValueTask(s.action(s.state, token)),
            cancellationToken);
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

    /// <summary>Executes a context-aware <see cref="Task{TResult}"/> delegate using state inherited from <paramref name="parentContext"/>.</summary>
    public static ValueTask<TResult> ExecuteWithContextAsync<TResult>(
        this Shield<TResult> shield,
        Func<KevlarContext, Task<TResult>> action,
        KevlarContext parentContext)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        return shield.ExecuteWithContextAsync(action, static (a, context) => new ValueTask<TResult>(a(context)), parentContext);
    }

    /// <summary>Executes a context-aware <see cref="Task{TResult}"/> delegate using state inherited from <paramref name="parentContext"/>, threading <paramref name="state"/> to avoid closure allocations.</summary>
    public static ValueTask<TResult> ExecuteWithContextAsync<TResult, TState>(
        this Shield<TResult> shield,
        TState state,
        Func<TState, KevlarContext, Task<TResult>> action,
        KevlarContext parentContext)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(parentContext, nameof(parentContext));
        return shield.ExecuteWithContextAsync(
            (state, action),
            static (s, context) => new ValueTask<TResult>(s.action(s.state, context)),
            parentContext);
    }

    /// <summary>Initializes execution properties, then executes a context-aware <see cref="Task{TResult}"/>-returning delegate.</summary>
    public static ValueTask<TResult> ExecuteWithContextAsync<TResult, TState>(
        this Shield<TResult> shield,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteWithContextAsync(
            (state, initializeProperties, action),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static (s, context) => new ValueTask<TResult>(s.action(s.state, context)),
            cancellationToken);
    }

    /// <summary>
    /// Initializes execution properties, executes a context-aware <see cref="Task{TResult}"/> delegate,
    /// then exposes final properties before the pooled context is returned.
    /// </summary>
    public static ValueTask<TResult> ExecuteWithContextAsync<TResult, TState>(
        this Shield<TResult> shield,
        TState state,
        Action<TState, KevlarProperties> initializeProperties,
        Func<TState, KevlarContext, Task<TResult>> action,
        Action<TState, KevlarProperties> onCompleted,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(initializeProperties, nameof(initializeProperties));
        Throw.IfNull(action, nameof(action));
        Throw.IfNull(onCompleted, nameof(onCompleted));
        return shield.ExecuteWithContextAsync(
            (state, initializeProperties, action, onCompleted),
            static (s, properties) => s.initializeProperties(s.state, properties),
            static (s, context) => new ValueTask<TResult>(s.action(s.state, context)),
            static (s, properties) => s.onCompleted(s.state, properties),
            cancellationToken);
    }

    /// <summary>
    /// Executes a context-aware <see cref="Task{TResult}"/>-returning delegate without seeding
    /// execution properties. The context is pooled; never retain it beyond the delegate.
    /// </summary>
    public static ValueTask<TResult> ExecuteWithContextAsync<TResult>(
        this Shield<TResult> shield,
        Func<KevlarContext, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteWithContextAsync(
            action,
            static (_, _) => { },
            static (a, context) => new ValueTask<TResult>(a(context)),
            cancellationToken);
    }

    /// <summary>Executes the <see cref="Task{TResult}"/>-returning delegate through the pipeline and returns the outcome instead of throwing.</summary>
    public static ValueTask<Outcome<TResult>> ExecuteOutcomeAsync<TResult>(this Shield<TResult> shield, Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteOutcomeAsync(token => new ValueTask<TResult>(action(token)), cancellationToken);
    }

    /// <summary>Executes the <see cref="Task{TResult}"/>-returning delegate through the pipeline, threading <paramref name="state"/> to avoid closure allocations, and returns the outcome instead of throwing.</summary>
    public static ValueTask<Outcome<TResult>> ExecuteOutcomeAsync<TResult, TState>(this Shield<TResult> shield, TState state, Func<TState, CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(shield, nameof(shield));
        Throw.IfNull(action, nameof(action));
        return shield.ExecuteOutcomeAsync((state, action), static (s, token) => new ValueTask<TResult>(s.action(s.state, token)), cancellationToken);
    }
}

#pragma warning restore RS0026

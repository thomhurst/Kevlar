using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// A result-type-safe factory for selecting a different operation for each hedged attempt.
/// Create one with <see cref="Create{TResult}"/> or the void <see cref="Create(Func{HedgeActionGeneratorEvent, Func{CancellationToken, ValueTask}})"/> overload.
/// </summary>
public sealed class HedgeActionGenerator
{
    private readonly Delegate _generator;
    private readonly Type _resultType;

    private HedgeActionGenerator(Delegate generator, Type resultType)
    {
        _generator = generator;
        _resultType = resultType;
    }

    /// <summary>Creates a generator for operations returning <typeparamref name="TResult"/>.</summary>
    /// <remarks>
    /// Return <see langword="null"/> to run <see cref="HedgeActionGeneratorEvent{TResult}.OriginalAction"/>.
    /// The returned operation receives the isolated attempt cancellation token.
    /// </remarks>
    public static HedgeActionGenerator Create<TResult>(
        Func<HedgeActionGeneratorEvent<TResult>, Func<CancellationToken, ValueTask<TResult>>?> generator)
    {
        Throw.IfNull(generator, nameof(generator));
        return new HedgeActionGenerator(generator, typeof(TResult));
    }

    /// <summary>Creates a generator for void-returning operations.</summary>
    /// <remarks>
    /// Return <see langword="null"/> to run <see cref="HedgeActionGeneratorEvent.OriginalAction"/>.
    /// The returned operation receives the isolated attempt cancellation token.
    /// </remarks>
    public static HedgeActionGenerator Create(
        Func<HedgeActionGeneratorEvent, Func<CancellationToken, ValueTask>?> generator)
    {
        Throw.IfNull(generator, nameof(generator));
        var adapter = new VoidGeneratorAdapter(generator);
        return new HedgeActionGenerator(adapter.Generate, typeof(Nothing));
    }

    internal Func<CancellationToken, ValueTask<TResult>>? Generate<TResult>(
        int attempt,
        KevlarContext context,
        Func<CancellationToken, ValueTask<TResult>> originalAction)
    {
        if (_resultType != typeof(TResult))
        {
            throw new InvalidOperationException(
                $"The hedge action generator was created for '{_resultType}', but this execution returns '{typeof(TResult)}'. " +
                "Create the generator with the execution's result type.");
        }

        var generator = (Func<HedgeActionGeneratorEvent<TResult>, Func<CancellationToken, ValueTask<TResult>>?>)_generator;
        return generator(new HedgeActionGeneratorEvent<TResult>(attempt, context, originalAction));
    }

    private sealed class VoidGeneratorAdapter(
        Func<HedgeActionGeneratorEvent, Func<CancellationToken, ValueTask>?> generator)
    {
        public Func<CancellationToken, ValueTask<Nothing>>? Generate(
            HedgeActionGeneratorEvent<Nothing> hedgeEvent)
        {
            var original = new VoidOriginalActionAdapter(hedgeEvent.OriginalAction);
            var action = generator(new HedgeActionGeneratorEvent(
                hedgeEvent.Attempt,
                hedgeEvent.Context,
                original.Invoke));
            return action is null ? null : new VoidActionAdapter(action).Invoke;
        }
    }

    private sealed class VoidOriginalActionAdapter(Func<CancellationToken, ValueTask<Nothing>> action)
    {
        public async ValueTask Invoke(CancellationToken cancellationToken) =>
            _ = await action(cancellationToken).ConfigureAwait(false);
    }

    private sealed class VoidActionAdapter(Func<CancellationToken, ValueTask> action)
    {
        public async ValueTask<Nothing> Invoke(CancellationToken cancellationToken)
        {
            await action(cancellationToken).ConfigureAwait(false);
            return Nothing.Value;
        }
    }
}

/// <summary>Arguments used to select a result-returning operation for a hedged attempt.</summary>
public readonly struct HedgeActionGeneratorEvent<TResult>
{
    internal HedgeActionGeneratorEvent(
        int attempt,
        KevlarContext context,
        Func<CancellationToken, ValueTask<TResult>> originalAction)
    {
        Attempt = attempt;
        Context = context;
        OriginalAction = originalAction;
    }

    /// <summary>The 1-based attempt number (2 = first hedge).</summary>
    public int Attempt { get; }

    /// <summary>The isolated context that belongs to this attempt.</summary>
    public KevlarContext Context { get; }

    /// <summary>The original operation, including strategies nested inside the hedge.</summary>
    public Func<CancellationToken, ValueTask<TResult>> OriginalAction { get; }
}

/// <summary>Arguments used to select a void-returning operation for a hedged attempt.</summary>
public readonly struct HedgeActionGeneratorEvent
{
    internal HedgeActionGeneratorEvent(
        int attempt,
        KevlarContext context,
        Func<CancellationToken, ValueTask> originalAction)
    {
        Attempt = attempt;
        Context = context;
        OriginalAction = originalAction;
    }

    /// <summary>The 1-based attempt number (2 = first hedge).</summary>
    public int Attempt { get; }

    /// <summary>The isolated context that belongs to this attempt.</summary>
    public KevlarContext Context { get; }

    /// <summary>The original operation, including strategies nested inside the hedge.</summary>
    public Func<CancellationToken, ValueTask> OriginalAction { get; }
}

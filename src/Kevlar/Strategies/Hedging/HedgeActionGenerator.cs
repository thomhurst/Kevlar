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
        Func<CancellationToken, ValueTask<TResult>> originalAction,
        Outcome<TResult>? outcome)
    {
        if (_resultType != typeof(TResult))
        {
            throw new InvalidOperationException(
                $"The hedge action generator was created for '{_resultType}', but this execution returns '{typeof(TResult)}'. " +
                "Create the generator with the execution's result type.");
        }

        var generator = (Func<HedgeActionGeneratorEvent<TResult>, Func<CancellationToken, ValueTask<TResult>>?>)_generator;
        return generator(new HedgeActionGeneratorEvent<TResult>(attempt, context, originalAction, outcome));
    }

    internal void ValidateResultType(Type resultType)
    {
        if (_resultType != resultType)
        {
            throw new InvalidOperationException(
                $"The hedge action generator was created for '{_resultType}', but this shield returns '{resultType}'. " +
                "Create the generator with the shield's result type.");
        }
    }

    private sealed class VoidGeneratorAdapter(
        Func<HedgeActionGeneratorEvent, Func<CancellationToken, ValueTask>?> generator)
    {
        public Func<CancellationToken, ValueTask<Nothing>>? Generate(
            HedgeActionGeneratorEvent<Nothing> hedgeEvent)
        {
            var original = new VoidOriginalActionAdapter(hedgeEvent.OriginalAction);
            var action = generator(new HedgeActionGeneratorEvent(
                hedgeEvent.AttemptNumber,
                hedgeEvent.Context,
                original.Invoke));
            return action is null ? null : new VoidActionAdapter(action).Invoke;
        }
    }

    private sealed class VoidOriginalActionAdapter(Func<CancellationToken, ValueTask<Nothing>> action)
    {
        public async ValueTask Invoke(CancellationToken cancellationToken)
        {
            // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
            _ = await action(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class VoidActionAdapter(Func<CancellationToken, ValueTask> action)
    {
        public async ValueTask<Nothing> Invoke(CancellationToken cancellationToken)
        {
            // Stryker disable once all: ConfigureAwait is execution-context policy, not outcome behavior.
            await action(cancellationToken).ConfigureAwait(false);
            return Nothing.Value;
        }
    }
}

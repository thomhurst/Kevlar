using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class HedgeActionGeneratorAdapter
{
    private readonly Delegate _generator;
    private readonly Type _resultType;

    private HedgeActionGeneratorAdapter(Delegate generator, Type resultType)
    {
        _generator = generator;
        _resultType = resultType;
    }

    public static HedgeActionGeneratorAdapter Create<TResult>(
        Func<HedgeActionGeneratorEvent<TResult>, Func<CancellationToken, ValueTask<TResult>>?> generator) =>
        new(generator, typeof(TResult));

    public static HedgeActionGeneratorAdapter Create(
        Func<HedgeActionGeneratorEvent, Func<CancellationToken, ValueTask>?> generator)
    {
        var adapter = new VoidGeneratorAdapter(generator);
        return new HedgeActionGeneratorAdapter(adapter.Generate, typeof(Nothing));
    }

    public Func<CancellationToken, ValueTask<TResult>>? Generate<TResult>(
        int attempt,
        KevlarContext context,
        Func<CancellationToken, ValueTask<TResult>> originalAction,
        Outcome<TResult>? outcome)
    {
        if (_resultType != typeof(TResult))
        {
            throw new InvalidOperationException(
                $"The hedge action generator was configured for '{_resultType}', but this execution returns '{typeof(TResult)}'.");
        }

        var generator = (Func<HedgeActionGeneratorEvent<TResult>, Func<CancellationToken, ValueTask<TResult>>?>)_generator;
        return generator(new HedgeActionGeneratorEvent<TResult>(attempt, context, originalAction, outcome));
    }

    public void ValidateResultType(Type resultType)
    {
        if (_resultType == resultType)
        {
            return;
        }

        if (_resultType == typeof(Nothing))
        {
            throw new InvalidOperationException(
                "The untyped hedge action generator can only be used for void execution. " +
                "Configure HedgeOptions<TResult>.ActionGenerator on a typed shield.");
        }

        throw new InvalidOperationException(
            $"The hedge action generator was configured for '{_resultType}', but this shield returns '{resultType}'.");
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

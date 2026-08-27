namespace Kevlar.Internal;

internal static class OutcomeDisposer
{
    public static bool IsSameResult<T>(in Outcome<T> candidate, in Outcome<T> selected)
    {
        if (!candidate.IsSuccess || !selected.IsSuccess)
        {
            return false;
        }

        if (!typeof(T).IsValueType)
        {
            return ReferenceEquals(candidate.Result, selected.Result);
        }

        try
        {
            return EqualityComparer<T>.Default.Equals(candidate.Result!, selected.Result!);
        }
        catch
        {
            return false;
        }
    }

    public static ValueTask DisposeResultAsync<T>(in Outcome<T> outcome, KevlarContext context)
    {
        if (!outcome.IsSuccess || !DisposalTraits<T>.MayRequireDisposal)
        {
            return default;
        }

        if (outcome.Result is IAsyncDisposable asyncDisposable)
        {
            return DisposeAsync(asyncDisposable, context);
        }

        if (outcome.Result is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                KevlarDiagnostics.ReportCallbackError(
                    CallbackErrorKind.ResultDisposal,
                    context,
                    exception,
                    nameof(OutcomeDisposer));
            }
        }

        return default;
    }

    private static ValueTask DisposeAsync(IAsyncDisposable disposable, KevlarContext context)
    {
        ValueTask disposal;
        try
        {
            disposal = disposable.DisposeAsync();
        }
        catch (Exception exception)
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.ResultDisposal,
                context,
                exception,
                nameof(OutcomeDisposer));
            return default;
        }

        if (disposal.IsCompletedSuccessfully)
        {
            disposal.GetAwaiter().GetResult();
            return default;
        }

        return AwaitDisposalAsync(disposal, context);
    }

    private static async ValueTask AwaitDisposalAsync(ValueTask disposal, KevlarContext context)
    {
        try
        {
            await disposal.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.ResultDisposal,
                context,
                exception,
                nameof(OutcomeDisposer));
        }
    }

    private static class DisposalTraits<T>
    {
        public static readonly bool MayRequireDisposal =
            !typeof(T).IsValueType
            || typeof(IAsyncDisposable).IsAssignableFrom(typeof(T))
            || typeof(IDisposable).IsAssignableFrom(typeof(T));
    }
}

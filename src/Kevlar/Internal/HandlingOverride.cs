namespace Kevlar.Internal;

internal static class HandlingOverride
{
    internal static OutcomeJudge Resolve(
        Func<Exception, bool>? handlesException,
        Func<HandlingEvent, bool>? handlesExceptionWithContext,
        OutcomeJudge ambientJudge) =>
        handlesException is null && handlesExceptionWithContext is null
            ? ambientJudge
            : handlesExceptionWithContext is null
                ? new ExceptionJudge(handlesException!)
                : new ContextExceptionJudge(handlesException, handlesExceptionWithContext);

    internal static OutcomeJudge Resolve<TResult>(
        Func<Exception, bool>? handlesException,
        Func<TResult, bool>? handlesResult,
        Func<HandlingEvent<TResult>, bool>? handlesExceptionWithContext,
        Func<HandlingEvent<TResult>, bool>? handlesResultWithContext,
        OutcomeJudge ambientJudge) =>
        handlesException is null
            && handlesResult is null
            && handlesExceptionWithContext is null
            && handlesResultWithContext is null
            ? ambientJudge
            : new TypedJudge<TResult>(
                handlesException,
                handlesResult,
                contextPredicate: Combine(handlesExceptionWithContext, handlesResultWithContext));

    private static Func<HandlingEvent<TResult>, bool>? Combine<TResult>(
        Func<HandlingEvent<TResult>, bool>? exceptionPredicate,
        Func<HandlingEvent<TResult>, bool>? resultPredicate)
    {
        if (exceptionPredicate is null)
        {
            return resultPredicate is null
                ? null
                : handling => handling.Outcome.Exception is null && resultPredicate(handling);
        }

        if (resultPredicate is null)
        {
            return handling => handling.Outcome.Exception is not null && exceptionPredicate(handling);
        }

        return handling => handling.Outcome.Exception is not null
            ? exceptionPredicate(handling)
            : resultPredicate(handling);
    }
}

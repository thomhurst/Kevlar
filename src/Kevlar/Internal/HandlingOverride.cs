namespace Kevlar.Internal;

internal static class HandlingOverride
{
    internal static OutcomeJudge Resolve(
        Func<Exception, bool>? handlesException,
        OutcomeJudge ambientJudge) =>
        handlesException is null
            ? ambientJudge
            : new ExceptionJudge(handlesException);

    internal static OutcomeJudge Resolve<TResult>(
        Func<Exception, bool>? handlesException,
        Func<TResult, bool>? handlesResult,
        OutcomeJudge ambientJudge) =>
        handlesException is null && handlesResult is null
            ? ambientJudge
            : new TypedJudge<TResult>(handlesException, handlesResult);
}

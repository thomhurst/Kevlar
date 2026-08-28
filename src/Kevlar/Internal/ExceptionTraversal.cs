namespace Kevlar.Internal;

/// <summary>Matches exception types throughout ordinary and aggregate inner-exception graphs.</summary>
internal static class ExceptionTraversal
{
    public static bool Matches<TException>(Exception exception)
        where TException : Exception
    {
        if (exception is TException)
        {
            return true;
        }

        return MatchesInner<TException>(exception);
    }

    public static bool Matches<TException>(Exception exception, Func<TException, bool> predicate)
        where TException : Exception
    {
        if (exception is TException typed && predicate(typed))
        {
            return true;
        }

        return MatchesInner(exception, predicate);
    }

    private static bool MatchesInner<TException>(Exception exception)
        where TException : Exception
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (Matches<TException>(inner))
                {
                    return true;
                }
            }

            return false;
        }

        return exception.InnerException is { } innerException
            && Matches<TException>(innerException);
    }

    private static bool MatchesInner<TException>(
        Exception exception,
        Func<TException, bool> predicate)
        where TException : Exception
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (Matches(inner, predicate))
                {
                    return true;
                }
            }

            return false;
        }

        return exception.InnerException is { } innerException
            && Matches(innerException, predicate);
    }
}

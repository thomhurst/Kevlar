namespace Kevlar.Internal;

/// <summary>Matches exception types throughout ordinary and aggregate inner-exception graphs.</summary>
internal static class ExceptionTraversal
{
    public static bool Matches<TException>(Exception exception)
        where TException : Exception
        => MatchesCore<TException>(exception, predicate: null);

    public static bool Matches<TException>(Exception exception, Func<TException, bool> predicate)
        where TException : Exception
        => MatchesCore(exception, predicate);

    private static bool MatchesCore<TException>(
        Exception exception,
        Func<TException, bool>? predicate)
        where TException : Exception
    {
        Stack<Exception>? pendingBranches = null;

        while (true)
        {
            if (exception is TException typed && (predicate is null || predicate(typed)))
            {
                return true;
            }

            if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index > 0; index--)
                {
                    (pendingBranches ??= new()).Push(aggregate.InnerExceptions[index]);
                }

                exception = aggregate.InnerExceptions[0];
                continue;
            }

            if (exception.InnerException is { } innerException)
            {
                exception = innerException;
                continue;
            }

            if (pendingBranches is null || pendingBranches.Count == 0)
            {
                return false;
            }

            exception = pendingBranches.Pop();
        }
    }
}

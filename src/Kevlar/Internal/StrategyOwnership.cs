namespace Kevlar.Internal;

internal static class StrategyOwnership
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Strategy, Ownership> Owners =
        new();

    public static bool TryAcquire(Strategy strategy)
    {
        var ownership = Owners.GetValue(strategy, static _ => new Ownership());
        lock (ownership)
        {
            if (ownership.DisposalStarted)
            {
                return false;
            }

            ownership.OwnerCount++;
            return true;
        }
    }

    public static bool Release(Strategy strategy)
    {
        var ownership = Owners.GetValue(strategy, static _ => new Ownership());
        lock (ownership)
        {
            ownership.OwnerCount--;
            if (ownership.OwnerCount != 0)
            {
                return false;
            }

            ownership.DisposalStarted = true;
            return true;
        }
    }

    private sealed class Ownership
    {
        public int OwnerCount;

        public bool DisposalStarted;
    }
}

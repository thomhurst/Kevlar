using System.Runtime.CompilerServices;

namespace Kevlar.Extensions.DependencyInjection;

internal sealed class ShieldRetirement
{
    private readonly WeakReference<object> _owner;

    public ShieldRetirement(object owner, IShieldLifecycle shield)
    {
        _owner = new WeakReference<object>(owner);
        Strategies = Track(shield);
    }

    public Strategy[] Strategies { get; }

    public bool CanReclaim()
    {
        if (_owner.TryGetTarget(out _))
        {
            return false;
        }

        foreach (var strategy in Strategies)
        {
            if (strategy.ExecutionTracker!.ActiveExecutions != 0)
            {
                return false;
            }
        }

        return true;
    }

    public void Reclaim(
        Action<Exception> reportFailure,
        HashSet<Strategy> retainedOrClaimed,
        StrategyDisposalTracker disposalTracker)
    {
        for (var index = Strategies.Length - 1; index >= 0; index--)
        {
            var strategy = Strategies[index];
            if (!retainedOrClaimed.Add(strategy) || !disposalTracker.TryClaim(strategy))
            {
                continue;
            }

            try
            {
                Dispose(strategy);
            }
            catch (Exception exception)
            {
                reportFailure(exception);
            }
        }
    }

    public static HashSet<Strategy> CreateStrategySet() => new(ReferenceComparer.Instance);

    public static Strategy[] Track(IShieldLifecycle shield)
    {
        var seen = CreateStrategySet();
        var strategies = new List<Strategy>(shield.Strategies.Length);
        foreach (var strategy in shield.Strategies)
        {
            if (!seen.Add(strategy))
            {
                continue;
            }

            strategy.EnableExecutionTracking();
            strategies.Add(strategy);
        }

        return strategies.ToArray();
    }

    private static void Dispose(Strategy strategy)
    {
        if (strategy is IDisposable disposable)
        {
            disposable.Dispose();
        }
        else if (strategy is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<Strategy>
    {
        public static ReferenceComparer Instance { get; } = new();

        public bool Equals(Strategy? x, Strategy? y) => ReferenceEquals(x, y);

        public int GetHashCode(Strategy obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

using System.Runtime.CompilerServices;

namespace Kevlar.Extensions.DependencyInjection;

internal sealed class StrategyDisposalTracker
{
    private readonly ConditionalWeakTable<Strategy, DisposalClaim> _claims = new();

    public bool TryClaim(Strategy strategy)
    {
        var claim = _claims.GetValue(strategy, static _ => new DisposalClaim());
        return Interlocked.Exchange(ref claim.IsClaimed, 1) == 0;
    }

    private sealed class DisposalClaim
    {
        public int IsClaimed;
    }
}

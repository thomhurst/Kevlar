using System.Threading.RateLimiting;

namespace Kevlar.Extensions.RateLimiting;

/// <summary>Acquires a rate-limit lease for one shield execution.</summary>
/// <param name="permitCount">The configured number of permits requested per execution.</param>
/// <param name="context">The current execution context, including its cancellation token.</param>
/// <returns>The acquired or rejected lease. The adapter disposes it exactly once.</returns>
public delegate ValueTask<RateLimitLease> RateLimitLeaseAcquirer(
    int permitCount,
    KevlarContext context);

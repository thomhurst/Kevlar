using Reservoir;

namespace Kevlar.Internal;

// A timer keeps the runtime TimerQueue selected when first armed. Keeping sources
// with the renting thread avoids moving timer-backed sources between every worker.
internal static class TimeoutSourcePool
{
#if NET8_0_OR_GREATER
    [ThreadStatic]
    private static CancellationTokenSourcePool? _threadPool;

    internal static CancellationTokenSource RentLinked(CancellationToken upstreamToken)
    {
        // The owning Reservoir pool handles cross-thread async returns safely. Retain
        // a small nested-timeout working set per thread; excess sources are discarded.
        var pool = _threadPool ??= new CancellationTokenSourcePool(maxCapacity: 8);
        return pool.RentLinked(upstreamToken);
    }
#else
    internal static CancellationTokenSource RentLinked(CancellationToken upstreamToken) =>
        CancellationTokenSourcePool.Shared.RentLinked(upstreamToken);
#endif
}

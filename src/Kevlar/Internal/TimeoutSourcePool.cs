using Reservoir;

namespace Kevlar.Internal;

// A timer keeps the runtime TimerQueue selected when it is first armed. Selecting the
// pool by processor keeps synchronous timeout rentals near that queue instead of moving
// timer-backed sources between every worker through one shared pool.
internal static class TimeoutSourcePool
{
#if NET8_0_OR_GREATER
    private static readonly CancellationTokenSourcePool[] Pools = CreatePools();

    [ThreadStatic]
    private static CancellationTokenSourcePool? _threadPool;

    internal static CancellationTokenSource RentLinked(CancellationToken upstreamToken)
    {
        // Keep the choice after thread migration: switching to a cold pool would allocate
        // a new source/timer in an otherwise warmed, zero-allocation execution loop.
        var pool = _threadPool ??= Pools[(uint)Thread.GetCurrentProcessorId() % (uint)Pools.Length];
        return pool.RentLinked(upstreamToken);
    }

    private static CancellationTokenSourcePool[] CreatePools()
    {
        const int totalCapacity = 128;
        var count = Math.Min(Environment.ProcessorCount, totalCapacity);
        var pools = new CancellationTokenSourcePool[count];
        for (var index = 0; index < pools.Length; index++)
        {
            pools[index] = new CancellationTokenSourcePool(maxCapacity: totalCapacity / count);
        }
        return pools;
    }
#else
    // .NET Standard does not expose the processor identity used by runtime timer queues.
    internal static CancellationTokenSource RentLinked(CancellationToken upstreamToken) =>
        CancellationTokenSourcePool.Shared.RentLinked(upstreamToken);
#endif
}

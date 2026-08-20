namespace Kevlar.Internal;

internal static class SharedRandom
{
#if NET
    public static double NextDouble() => Random.Shared.NextDouble();
#else
    private static int _seed = Environment.TickCount;

    [ThreadStatic]
    private static Random? _local;

    public static double NextDouble() =>
        (_local ??= new Random(Interlocked.Increment(ref _seed))).NextDouble();
#endif
}

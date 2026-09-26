using Reservoir;

namespace Kevlar.Internal;

/// <summary>Keeps successful timeout sources local to each returning thread.</summary>
internal static class TimeoutSourcePool
{
#if NET8_0_OR_GREATER
    // A single shared fallback bounds spillover from nested timeouts. Reservoir tracks
    // the one-source thread-local tiers and safely handles returns on another thread.
    private static readonly ObjectPool<Source, Policy> Pool = new(
        default,
        ObjectPool<Source, Policy>.DefaultMaximumRetained,
        threadLocalFastPath: true);
#endif

    public static CancellationTokenSource RentLinked(CancellationToken upstreamToken)
    {
#if NET8_0_OR_GREATER
        var source = Pool.Rent();
        try
        {
            source.Link(upstreamToken);
            return source;
        }
        catch
        {
            source.DisposePermanently();
            throw;
        }
#else
        return CancellationTokenSourcePool.Shared.RentLinked(upstreamToken);
#endif
    }

#if NET8_0_OR_GREATER
    private sealed class Source : CancellationTokenSource
    {
        private CancellationTokenRegistration _upstream;

        public void Link(CancellationToken token)
        {
            if (token.CanBeCanceled)
            {
                _upstream = token.UnsafeRegister(static state => ((Source)state!).Cancel(), this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Wait for upstream cancellation before TryReset or reuse. A later caller
                // must never be cancelled through an earlier rental's registration.
                _upstream.Dispose();
                _upstream = default;
                Pool.Return(this);
            }
        }

        public void DisposePermanently()
        {
            _upstream.Dispose();
            _upstream = default;
            base.Dispose(disposing: true);
        }
    }

    private readonly struct Policy : IPooledObjectPolicy<Source>
    {
        public Source Create() => new();

        public bool TryReset(Source source) => source.TryReset();

        public void Destroy(Source source) => source.DisposePermanently();
    }
#endif
}

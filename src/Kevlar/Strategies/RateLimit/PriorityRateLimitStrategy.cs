using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class PriorityRateLimitStrategy : PriorityLimitStrategy, IRateLimitState
{
    internal RateLimitStrategy Configuration { get; }
    private readonly KevlarMetrics.StateMetricRegistration<IRateLimitState> _metrics;

    public PriorityRateLimitStrategy(RateLimitOptions options) : base(options.QueueLimit, options.QueueTimeout)
    {
        Configuration = new RateLimitStrategy(options);
        _metrics = KevlarMetrics.RegisterRateStateSource(this);
    }

    protected override bool ReleasesAfterExecution => false;
    public override string Describe() => Configuration.Describe().TrimEnd(')') + ", priority queue)";
    protected override bool TryAcquire(TimeProvider timeProvider, out TimeSpan? retryAfter) =>
        Configuration.TryAcquireWithoutQueue(timeProvider, out retryAfter, out _);
    protected override void Release() { }
    protected override ValueTask<Outcome<T>> RejectAsync<T>(KevlarContext context, TimeSpan? retryAfter, string? reason) =>
        Configuration.RejectAsync<T>(context, retryAfter, reason);

    protected override void RegisterMetrics(KevlarContext context)
    {
        if (KevlarMetrics.RateStateEnabled)
        {
            _metrics.Add(new StrategyMetricAlias(context.ShieldName, context.StrategyIndex), context.TimeProvider);
        }
    }

    public (long Available, int Queued) CaptureState(TimeProvider timeProvider)
    {
        lock (Gate)
        {
            return (Queued == 0 ? Configuration.CaptureState(timeProvider).Available : 0, Queued);
        }
    }

    internal (long Available, int Queued, IReadOnlyDictionary<int, int> Priorities) CapturePriorityState(TimeProvider timeProvider)
    {
        lock (Gate)
        {
            return (Queued == 0 ? Configuration.CaptureState(timeProvider).Available : 0, Queued, CapturePriorities());
        }
    }
}

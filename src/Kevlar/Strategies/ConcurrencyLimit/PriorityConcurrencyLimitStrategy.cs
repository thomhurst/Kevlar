using Kevlar.Internal;

namespace Kevlar.Strategies;

internal sealed class PriorityConcurrencyLimitStrategy : PriorityLimitStrategy, IConcurrencyLimitState
{
    internal ConcurrencyLimitStrategy Configuration { get; }
    private int _running;
    private readonly KevlarMetrics.StateMetricRegistration<IConcurrencyLimitState> _metrics;

    public PriorityConcurrencyLimitStrategy(ConcurrencyLimitOptions options) : base(options.QueueLimit, options.QueueTimeout)
    {
        Configuration = new ConcurrencyLimitStrategy(options);
        _metrics = KevlarMetrics.RegisterConcurrencyStateSource(this);
    }

    public int MaxConcurrency => Configuration.MaxConcurrency;
    public int CurrentLimit => MaxConcurrency;
    protected override bool ReleasesAfterExecution => true;
    public override string Describe() => Configuration.Describe().TrimEnd(')') + ", priority queue)";

    protected override bool TryAcquire(TimeProvider timeProvider, out TimeSpan? retryAfter)
    {
        retryAfter = null;
        if (_running >= MaxConcurrency)
        {
            return false;
        }
        _running++;
        return true;
    }

    protected override void Release() => _running--;
    protected override ValueTask<Outcome<T>> RejectAsync<T>(KevlarContext context, TimeSpan? retryAfter, string? reason) =>
        Configuration.RejectAsync<T>(context, reason);

    protected override void RegisterMetrics(KevlarContext context)
    {
        if (KevlarMetrics.ConcurrencyStateEnabled)
        {
            _metrics.Add(new StrategyMetricAlias(context.ShieldName, context.StrategyIndex));
        }
    }

    public (int Available, int Running, int Queued) CaptureState()
    {
        lock (Gate)
        {
            return (MaxConcurrency - _running, _running, Queued);
        }
    }

    internal (int Running, int Queued, IReadOnlyDictionary<int, int> Priorities) CapturePriorityState()
    {
        lock (Gate)
        {
            return (_running, Queued, CapturePriorities());
        }
    }
}

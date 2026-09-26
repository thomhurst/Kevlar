namespace Kevlar;

/// <summary>Shares a feedback throttle or replenishing attempt allowance across retries and hedges.</summary>
/// <remarks>
/// The constructor creates a feedback throttle. Its balance starts at <see cref="MaxTokens"/>. A handled failure subtracts one token;
/// an acceptable successful result adds <see cref="TokenRatio"/>, bounded by the capacity.
/// Additional attempts are allowed only above half capacity. Initial attempts are never gated.
/// Use <see cref="CreateReplenishing"/> for an atomic allowance consumed by additional attempts instead.
/// Neither mode limits initial attempts or the number of attempts already in flight.
/// </remarks>
public sealed class RetryBudget
{
    private const long TokenScale = 1000;
    private readonly long _capacity;
    private readonly long _refund;
    private readonly ReplenishingAllowance? _allowance;
    private long _tokens;

    /// <summary>Creates a shared budget with positive capacity and a finite success refund of at least 0.001.</summary>
    /// <param name="maxTokens">Maximum token balance. Default 100.</param>
    /// <param name="tokenRatio">Tokens restored by each acceptable success, rounded down to three decimal places and capped at capacity. Default 0.1.</param>
    public RetryBudget(int maxTokens = 100, double tokenRatio = 0.1)
    {
        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Capacity must be positive.");
        }
        if (double.IsNaN(tokenRatio) || double.IsInfinity(tokenRatio) || tokenRatio < 0.001)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenRatio), "Success refund must be finite and at least 0.001.");
        }

        MaxTokens = maxTokens;
        _capacity = maxTokens * TokenScale;
        _refund = (long)decimal.Truncate((decimal)Math.Min(tokenRatio, maxTokens) * TokenScale);
        TokenRatio = _refund / (double)TokenScale;
        _tokens = _capacity;
    }

    private RetryBudget(int maxTokens, TimeSpan replenishmentPeriod, TimeProvider timeProvider)
        : this(maxTokens)
    {
        if (replenishmentPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(replenishmentPeriod), "Replenishment period must be positive.");
        }
        TokenRatio = 0;
        _allowance = new ReplenishingAllowance(maxTokens, replenishmentPeriod, timeProvider);
    }

    /// <summary>Creates an atomic allowance for additional attempts, replenished in fixed monotonic windows.</summary>
    /// <param name="maxTokens">Positive number of additional attempts available in each window.</param>
    /// <param name="replenishmentPeriod">Positive window duration, anchored at budget creation.</param>
    /// <param name="timeProvider">Clock owned by the budget, independent of shield clocks. Null uses <see cref="TimeProvider.System"/>.</param>
    /// <returns>A shared budget that starts full and refills lazily without a background timer.</returns>
    /// <remarks>
    /// Initial attempts are free. Each admitted retry or hedge consumes one token before its continuation or generated action starts.
    /// Cancellation before admission consumes nothing; cancellation or downstream rejection after admission does not refund the token.
    /// Outcomes do not change the balance. Nested strategies charge only their own additional attempts.
    /// Fixed windows can admit bursts across a boundary; combine with rate or concurrency limits when required.
    /// </remarks>
    public static RetryBudget CreateReplenishing(int maxTokens, TimeSpan replenishmentPeriod, TimeProvider? timeProvider = null) =>
        new(maxTokens, replenishmentPeriod, timeProvider ?? TimeProvider.System);

    /// <summary>The maximum token balance.</summary>
    public int MaxTokens { get; }

    /// <summary>The token refund for an acceptable successful result, or zero for a replenishing allowance.</summary>
    public double TokenRatio { get; }

    /// <summary>The fixed replenishment period, or null for a feedback throttle.</summary>
    public TimeSpan? ReplenishmentPeriod => _allowance?.Period;

    /// <summary>A thread-safe snapshot of the current balance, between zero and <see cref="MaxTokens"/>.</summary>
    public double Tokens => _allowance is { } allowance
        ? allowance.Tokens
        : Volatile.Read(ref _tokens) / (double)TokenScale;

    /// <summary>Whether a feedback balance exceeds half capacity, or a replenishing allowance has a token available.</summary>
    /// <remarks>This is a snapshot, not a reservation. Strategies atomically acquire replenishing tokens at admission.</remarks>
    public bool AllowsAdditionalAttempt => _allowance is { } allowance
        ? allowance.Tokens > 0
        : Volatile.Read(ref _tokens) > _capacity / 2;

    internal bool TryAcquireAdditionalAttempt() => _allowance?.TryAcquire() ?? AllowsAdditionalAttempt;

    internal void Observe<T>(in Outcome<T> outcome, bool handled, KevlarContext context)
    {
        if (_allowance is not null)
        {
            return;
        }
        if (outcome.Exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (!handled && !outcome.IsSuccess)
        {
            return;
        }

        var adjustment = handled ? -TokenScale : _refund;
        while (true)
        {
            var current = Volatile.Read(ref _tokens);
            var updated = Math.Max(0, Math.Min(_capacity, current + adjustment));
            if (updated == current || Interlocked.CompareExchange(ref _tokens, updated, current) == current)
            {
                return;
            }
        }
    }

    private sealed class ReplenishingAllowance(int capacity, TimeSpan period, TimeProvider timeProvider)
    {
        private readonly object _gate = new();
        private readonly int _capacity = capacity;
        private readonly long _startedAt = timeProvider.GetTimestamp();
        private long _window;
        private int _tokens = capacity;

        internal TimeSpan Period => period;

        internal int Tokens
        {
            get
            {
                lock (_gate)
                {
                    Replenish();
                    return _tokens;
                }
            }
        }

        internal bool TryAcquire()
        {
            lock (_gate)
            {
                Replenish();
                if (_tokens == 0)
                {
                    return false;
                }
                _tokens--;
                return true;
            }
        }

        private void Replenish()
        {
            var window = timeProvider.GetElapsedTime(_startedAt, timeProvider.GetTimestamp()).Ticks / period.Ticks;
            if (window > _window)
            {
                _window = window;
                _tokens = _capacity;
            }
        }
    }
}

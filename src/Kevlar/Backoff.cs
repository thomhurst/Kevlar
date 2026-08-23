using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Computes the delay before each retry attempt. Create instances through the static factories.
/// </summary>
public abstract class Backoff
{
    private protected Backoff()
    {
    }

    /// <summary>No delay between attempts.</summary>
    public static Backoff None { get; } = new ConstantBackoff(TimeSpan.Zero);

    /// <summary>
    /// Kevlar's default: exponential backoff starting at 250ms with a factor of 2, ±50% jitter,
    /// capped at 30 seconds.
    /// </summary>
    public static Backoff Default { get; } = Exponential(TimeSpan.FromMilliseconds(250), maxDelay: TimeSpan.FromSeconds(30));

    /// <summary>The same delay before every attempt.</summary>
    public static Backoff Constant(TimeSpan delay)
    {
        Throw.IfOutOfRange(delay < TimeSpan.Zero, nameof(delay), "Delay must not be negative.");
        Throw.IfOutOfRange(delay > DelayHelper.MaximumDelay, nameof(delay), "Delay exceeds the runtime timer limit.");
        return new ConstantBackoff(delay);
    }

    /// <summary>Linearly increasing delay: <paramref name="step"/>, 2×step, 3×step…</summary>
    public static Backoff Linear(TimeSpan step, TimeSpan? maxDelay = null)
    {
        Throw.IfOutOfRange(step < TimeSpan.Zero, nameof(step), "Step must not be negative.");
        ValidateMaxDelay(maxDelay);
        return new LinearBackoff(step, maxDelay);
    }

    /// <summary>
    /// Exponentially increasing delay: <paramref name="initialDelay"/>, then multiplied by
    /// <paramref name="factor"/> after each attempt. With <paramref name="jitter"/> enabled
    /// (the default) each delay is scaled by a random factor in [0.5, 1.5) to avoid
    /// synchronized retry storms.
    /// </summary>
    public static Backoff Exponential(TimeSpan initialDelay, double factor = 2.0, TimeSpan? maxDelay = null, bool jitter = true)
    {
        Throw.IfOutOfRange(initialDelay < TimeSpan.Zero, nameof(initialDelay), "Initial delay must not be negative.");
        Throw.IfOutOfRange(
            factor < 1.0 || double.IsNaN(factor) || double.IsInfinity(factor),
            nameof(factor),
            "Factor must be finite and at least 1.");
        ValidateMaxDelay(maxDelay);
        return new ExponentialBackoff(initialDelay, factor, maxDelay, jitter);
    }

    /// <summary>A caller-supplied delay function receiving the 1-based retry attempt number.</summary>
    /// <remarks>
    /// The returned delay is clamped rather than trusted: a negative delay becomes
    /// <see cref="TimeSpan.Zero"/> and anything above the runtime timer limit
    /// (<c>uint.MaxValue - 1</c> milliseconds, roughly 49 days) becomes that limit. A retry's own
    /// <see cref="RetryOptions.MaxDelay"/> then caps what is left, so an arithmetic slip in
    /// <paramref name="delayFactory"/> cannot wedge a pipeline on a delay it can never wait out.
    /// </remarks>
    public static Backoff Custom(Func<int, TimeSpan> delayFactory)
    {
        Throw.IfNull(delayFactory, nameof(delayFactory));
        return new CustomBackoff(delayFactory);
    }

    /// <summary>Returns the delay before the given 1-based retry attempt.</summary>
    public abstract TimeSpan GetDelay(int attempt);

    internal abstract BackoffConfigurationKind ConfigurationKind { get; }

    internal virtual TimeSpan? BaseDelay => null;

    internal virtual double? Factor => null;

    internal virtual TimeSpan? MaxDelay => null;

    internal virtual bool? Jitter => null;

    private protected static void ValidateAttempt(int attempt) =>
        Throw.IfOutOfRange(attempt < 1, nameof(attempt), "Attempt must be at least 1.");

    private static void ValidateMaxDelay(TimeSpan? maxDelay)
    {
        Throw.IfOutOfRange(maxDelay.HasValue && maxDelay.Value < TimeSpan.Zero, nameof(maxDelay), "Maximum delay must not be negative.");
        Throw.IfOutOfRange(maxDelay > DelayHelper.MaximumDelay, nameof(maxDelay), "Maximum delay exceeds the runtime timer limit.");
    }

    private static TimeSpan FromTicksClamped(double ticks, TimeSpan? maxDelay)
    {
        if (double.IsNaN(ticks))
        {
            return TimeSpan.Zero;
        }

        var max = maxDelay ?? TimeSpan.FromDays(1);
        if (ticks >= max.Ticks)
        {
            return max;
        }

        return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)ticks);
    }

    private sealed class ConstantBackoff : Backoff
    {
        private readonly TimeSpan _delay;

        public ConstantBackoff(TimeSpan delay) => _delay = delay;

        public override TimeSpan GetDelay(int attempt)
        {
            ValidateAttempt(attempt);
            return _delay;
        }

        internal override BackoffConfigurationKind ConfigurationKind =>
            _delay == TimeSpan.Zero ? BackoffConfigurationKind.None : BackoffConfigurationKind.Constant;

        internal override TimeSpan? BaseDelay => _delay;

        public override string ToString() =>
            _delay == TimeSpan.Zero ? "no delay" : $"constant {DescribeHelper.Time(_delay)}";
    }

    private sealed class LinearBackoff : Backoff
    {
        private readonly TimeSpan _step;
        private readonly TimeSpan? _maxDelay;

        public LinearBackoff(TimeSpan step, TimeSpan? maxDelay)
        {
            _step = step;
            _maxDelay = maxDelay;
        }

        public override TimeSpan GetDelay(int attempt)
        {
            ValidateAttempt(attempt);
            return FromTicksClamped((double)_step.Ticks * attempt, _maxDelay);
        }

        internal override BackoffConfigurationKind ConfigurationKind => BackoffConfigurationKind.Linear;

        internal override TimeSpan? BaseDelay => _step;

        internal override TimeSpan? MaxDelay => _maxDelay;

        public override string ToString()
        {
            var cap = _maxDelay is { } max ? $" ≤{DescribeHelper.Time(max)}" : string.Empty;
            return $"linear {DescribeHelper.Time(_step)} steps{cap}";
        }
    }

    private sealed class ExponentialBackoff : Backoff
    {
        private readonly TimeSpan _initialDelay;
        private readonly double _factor;
        private readonly TimeSpan? _maxDelay;
        private readonly bool _jitter;

        public ExponentialBackoff(TimeSpan initialDelay, double factor, TimeSpan? maxDelay, bool jitter)
        {
            _initialDelay = initialDelay;
            _factor = factor;
            _maxDelay = maxDelay;
            _jitter = jitter;
        }

        public override TimeSpan GetDelay(int attempt)
        {
            ValidateAttempt(attempt);
            var ticks = _initialDelay.Ticks * Math.Pow(_factor, attempt - 1);

            if (_jitter)
            {
                ticks *= 0.5 + SharedRandom.NextDouble();
            }

            return FromTicksClamped(ticks, _maxDelay);
        }

        internal override BackoffConfigurationKind ConfigurationKind => BackoffConfigurationKind.Exponential;

        internal override TimeSpan? BaseDelay => _initialDelay;

        internal override double? Factor => _factor;

        internal override TimeSpan? MaxDelay => _maxDelay;

        internal override bool? Jitter => _jitter;

        public override string ToString()
        {
            var jitter = _jitter ? " +jitter" : string.Empty;
            var cap = _maxDelay is { } max ? $" ≤{DescribeHelper.Time(max)}" : string.Empty;
            return FormattableString.Invariant($"exponential {DescribeHelper.Time(_initialDelay)} ×{_factor:0.#}{jitter}{cap}");
        }
    }

    private sealed class CustomBackoff : Backoff
    {
        private readonly Func<int, TimeSpan> _delayFactory;

        public CustomBackoff(Func<int, TimeSpan> delayFactory) => _delayFactory = delayFactory;

        public override TimeSpan GetDelay(int attempt)
        {
            ValidateAttempt(attempt);
            var delay = _delayFactory(attempt);
            return delay < TimeSpan.Zero ? TimeSpan.Zero : DelayHelper.Clamp(delay);
        }

        internal override BackoffConfigurationKind ConfigurationKind => BackoffConfigurationKind.Custom;

        public override string ToString() => "custom backoff";
    }
}

internal enum BackoffConfigurationKind
{
    None,
    Constant,
    Linear,
    Exponential,
    Custom,
}

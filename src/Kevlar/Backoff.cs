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
    public static Backoff None { get; } = new ConstantBackoff(TimeSpan.Zero, global::Kevlar.Jitter.None);

    /// <summary>
    /// Kevlar's default: exponential backoff starting at 250ms with a factor of 2, equal jitter,
    /// capped at 30 seconds.
    /// </summary>
    public static Backoff Default { get; } = Exponential(TimeSpan.FromMilliseconds(250), maxDelay: TimeSpan.FromSeconds(30));

    /// <summary>The same base delay before every attempt, with optional jitter.</summary>
    public static Backoff Constant(TimeSpan delay, Jitter jitter = global::Kevlar.Jitter.None)
    {
        Throw.IfOutOfRange(delay < TimeSpan.Zero, nameof(delay), "Delay must not be negative.");
        Throw.IfOutOfRange(delay > DelayHelper.MaximumDelay, nameof(delay), "Delay exceeds the runtime timer limit.");
        ValidateJitter(jitter);
        return new ConstantBackoff(delay, jitter);
    }

    /// <summary>Linearly increasing base delay: <paramref name="step"/>, 2×step, 3×step…, with optional jitter.</summary>
    public static Backoff Linear(
        TimeSpan step,
        TimeSpan? maxDelay = null,
        Jitter jitter = global::Kevlar.Jitter.None)
    {
        Throw.IfOutOfRange(step < TimeSpan.Zero, nameof(step), "Step must not be negative.");
        ValidateMaxDelay(maxDelay);
        ValidateJitter(jitter);
        return new LinearBackoff(step, maxDelay, jitter);
    }

    /// <summary>
    /// Exponentially increasing delay: <paramref name="initialDelay"/>, then multiplied by
    /// <paramref name="factor"/> after each attempt. The default equal jitter scales each delay
    /// by a random factor in [0.5, 1.5) to avoid synchronized retry storms. Decorrelated jitter
    /// instead selects each delay between the initial delay and three times the preceding delay.
    /// </summary>
    public static Backoff Exponential(
        TimeSpan initialDelay,
        double factor = 2.0,
        TimeSpan? maxDelay = null,
        Jitter jitter = global::Kevlar.Jitter.Equal)
    {
        Throw.IfOutOfRange(initialDelay < TimeSpan.Zero, nameof(initialDelay), "Initial delay must not be negative.");
        Throw.IfOutOfRange(
            factor < 1.0 || double.IsNaN(factor) || double.IsInfinity(factor),
            nameof(factor),
            "Factor must be finite and at least 1.");
        ValidateMaxDelay(maxDelay);
        ValidateJitter(jitter);
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

    /// <summary>The stable category of this backoff.</summary>
    public abstract BackoffKind Kind { get; }

    /// <summary>The constant delay, linear step, or exponential initial delay, when applicable.</summary>
    public virtual TimeSpan? InitialDelay => null;

    /// <summary>The exponential multiplier, when applicable.</summary>
    public virtual double? Factor => null;

    /// <summary>The linear or exponential delay cap, when configured.</summary>
    public virtual TimeSpan? MaxDelay => null;

    /// <summary>The jitter mode for built-in backoffs, when applicable.</summary>
    public virtual Jitter? Jitter => null;

    /// <summary>
    /// Returns the delay before the given retry attempt, using the preceding effective delay for
    /// stateful jitter modes. Pass <see cref="TimeSpan.Zero"/> for the first decorrelated draw.
    /// </summary>
    public virtual TimeSpan GetDelay(int attempt, TimeSpan previousDelay) => GetDelay(attempt);

    private protected static void ValidateAttempt(int attempt) =>
        Throw.IfOutOfRange(attempt < 1, nameof(attempt), "Attempt must be at least 1.");

    private static void ValidateMaxDelay(TimeSpan? maxDelay)
    {
        Throw.IfOutOfRange(maxDelay.HasValue && maxDelay.Value < TimeSpan.Zero, nameof(maxDelay), "Maximum delay must not be negative.");
        Throw.IfOutOfRange(maxDelay > DelayHelper.MaximumDelay, nameof(maxDelay), "Maximum delay exceeds the runtime timer limit.");
    }

    private static void ValidateJitter(Jitter jitter) =>
        Throw.IfOutOfRange(
            !Enum.IsDefined(typeof(Jitter), jitter),
            nameof(jitter),
            "Unknown jitter mode.");

    private static TimeSpan FromTicksClamped(double ticks, TimeSpan? maxDelay)
    {
        if (double.IsNaN(ticks))
        {
            return TimeSpan.Zero;
        }

        var max = maxDelay ?? DelayHelper.MaximumDelay;
        if (ticks >= max.Ticks)
        {
            return max;
        }

        return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)ticks);
    }

    private protected static double ApplyJitter(double ticks, Jitter jitter) => jitter switch
    {
        global::Kevlar.Jitter.None => ticks,
        global::Kevlar.Jitter.Equal => ticks * (0.5 + SharedRandom.NextDouble()),
        global::Kevlar.Jitter.Full => ticks * SharedRandom.NextDouble(),
        _ => ticks,
    };

    private protected static TimeSpan GetDecorrelatedDelay(
        int attempt,
        TimeSpan initialDelay,
        TimeSpan? maxDelay)
    {
        ValidateAttempt(attempt);
        var previousDelay = initialDelay;
        for (var currentAttempt = 0; currentAttempt < attempt; currentAttempt++)
        {
            previousDelay = GetDecorrelatedDelay(initialDelay, previousDelay, maxDelay);
        }

        return previousDelay;
    }

    private protected static TimeSpan GetDecorrelatedDelay(
        TimeSpan initialDelay,
        TimeSpan previousDelay,
        TimeSpan? maxDelay)
    {
        var lowerTicks = (double)initialDelay.Ticks;
        var previousTicks = previousDelay > TimeSpan.Zero ? previousDelay.Ticks : initialDelay.Ticks;
        var upperTicks = Math.Max(lowerTicks, previousTicks * 3d);
        return FromTicksClamped(
            lowerTicks + (SharedRandom.NextDouble() * (upperTicks - lowerTicks)),
            maxDelay);
    }

    private protected static string DescribeJitter(Jitter jitter) => jitter switch
    {
        global::Kevlar.Jitter.None => string.Empty,
        global::Kevlar.Jitter.Equal => ", equal jitter",
        global::Kevlar.Jitter.Full => ", full jitter",
        global::Kevlar.Jitter.Decorrelated => ", decorrelated jitter",
        _ => string.Empty,
    };

    private protected static string DescribeCap(TimeSpan? maxDelay) =>
        maxDelay is { } max ? $", cap {DescribeHelper.Time(max)}" : string.Empty;

    private sealed class ConstantBackoff : Backoff
    {
        private readonly TimeSpan _delay;
        private readonly Jitter _jitter;

        public ConstantBackoff(TimeSpan delay, Jitter jitter)
        {
            _delay = delay;
            _jitter = jitter;
        }

        public override TimeSpan GetDelay(int attempt)
        {
            ValidateAttempt(attempt);
            return _jitter == global::Kevlar.Jitter.Decorrelated
                ? GetDecorrelatedDelay(attempt, _delay, maxDelay: null)
                : FromTicksClamped(ApplyJitter(_delay.Ticks, _jitter), maxDelay: null);
        }

        public override TimeSpan GetDelay(int attempt, TimeSpan previousDelay)
        {
            ValidateAttempt(attempt);
            return _jitter == global::Kevlar.Jitter.Decorrelated
                ? GetDecorrelatedDelay(_delay, previousDelay, maxDelay: null)
                : GetDelay(attempt);
        }

        public override BackoffKind Kind =>
            _delay == TimeSpan.Zero ? BackoffKind.None : BackoffKind.Constant;

        public override TimeSpan? InitialDelay => _delay;

        public override Jitter? Jitter => _jitter;

        public override string ToString() =>
            _delay == TimeSpan.Zero
                ? "no delay"
                : $"constant {DescribeHelper.Time(_delay)}{DescribeJitter(_jitter)}";
    }

    private sealed class LinearBackoff : Backoff
    {
        private readonly TimeSpan _step;
        private readonly TimeSpan? _maxDelay;
        private readonly Jitter _jitter;

        public LinearBackoff(TimeSpan step, TimeSpan? maxDelay, Jitter jitter)
        {
            _step = step;
            _maxDelay = maxDelay;
            _jitter = jitter;
        }

        public override TimeSpan GetDelay(int attempt)
        {
            ValidateAttempt(attempt);
            return _jitter == global::Kevlar.Jitter.Decorrelated
                ? GetDecorrelatedDelay(attempt, _step, _maxDelay)
                : FromTicksClamped(ApplyJitter((double)_step.Ticks * attempt, _jitter), _maxDelay);
        }

        public override TimeSpan GetDelay(int attempt, TimeSpan previousDelay)
        {
            ValidateAttempt(attempt);
            return _jitter == global::Kevlar.Jitter.Decorrelated
                ? GetDecorrelatedDelay(_step, previousDelay, _maxDelay)
                : GetDelay(attempt);
        }

        public override BackoffKind Kind => BackoffKind.Linear;

        public override TimeSpan? InitialDelay => _step;

        public override TimeSpan? MaxDelay => _maxDelay;

        public override Jitter? Jitter => _jitter;

        public override string ToString() =>
            $"linear {DescribeHelper.Time(_step)} steps{DescribeJitter(_jitter)}{DescribeCap(_maxDelay)}";
    }

    private sealed class ExponentialBackoff : Backoff
    {
        private readonly TimeSpan _initialDelay;
        private readonly double _factor;
        private readonly TimeSpan? _maxDelay;
        private readonly Jitter _jitter;

        public ExponentialBackoff(TimeSpan initialDelay, double factor, TimeSpan? maxDelay, Jitter jitter)
        {
            _initialDelay = initialDelay;
            _factor = factor;
            _maxDelay = maxDelay;
            _jitter = jitter;
        }

        public override TimeSpan GetDelay(int attempt)
        {
            ValidateAttempt(attempt);
            if (_jitter == global::Kevlar.Jitter.Decorrelated)
            {
                return GetDecorrelatedDelay(attempt, _initialDelay, _maxDelay);
            }

            var ticks = _initialDelay.Ticks * Math.Pow(_factor, attempt - 1);
            return FromTicksClamped(ApplyJitter(ticks, _jitter), _maxDelay);
        }

        public override TimeSpan GetDelay(int attempt, TimeSpan previousDelay)
        {
            ValidateAttempt(attempt);
            return _jitter == global::Kevlar.Jitter.Decorrelated
                ? GetDecorrelatedDelay(_initialDelay, previousDelay, _maxDelay)
                : GetDelay(attempt);
        }

        public override BackoffKind Kind => BackoffKind.Exponential;

        public override TimeSpan? InitialDelay => _initialDelay;

        public override double? Factor => _factor;

        public override TimeSpan? MaxDelay => _maxDelay;

        public override Jitter? Jitter => _jitter;

        public override string ToString() => FormattableString.Invariant(
            $"exponential {DescribeHelper.Time(_initialDelay)} ×{_factor:0.#}{DescribeJitter(_jitter)}{DescribeCap(_maxDelay)}");
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

        public override BackoffKind Kind => BackoffKind.Custom;

        public override string ToString() => "custom backoff";
    }
}

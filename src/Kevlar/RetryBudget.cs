namespace Kevlar;

/// <summary>Shares success/failure feedback to throttle retries and hedges across executions.</summary>
/// <remarks>
/// The balance starts at <see cref="MaxTokens"/>. A handled failure subtracts one token;
/// an acceptable successful result adds <see cref="TokenRatio"/>, bounded by the capacity.
/// Additional attempts are allowed only above half capacity. Initial attempts are never gated.
/// This is a feedback throttle, not a rate limiter or a reservation for in-flight attempts.
/// </remarks>
public sealed class RetryBudget
{
    private const long TokenScale = 1000;
    private readonly long _capacity;
    private readonly long _refund;
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

    /// <summary>The maximum token balance.</summary>
    public int MaxTokens { get; }

    /// <summary>The token refund for an acceptable successful result.</summary>
    public double TokenRatio { get; }

    /// <summary>A thread-safe snapshot of the current balance, between zero and <see cref="MaxTokens"/>.</summary>
    public double Tokens => Volatile.Read(ref _tokens) / (double)TokenScale;

    /// <summary>Whether the current balance exceeds half capacity and permits an additional attempt.</summary>
    public bool AllowsAdditionalAttempt => Volatile.Read(ref _tokens) > _capacity / 2;

    internal void Observe<T>(in Outcome<T> outcome, bool handled, KevlarContext context)
    {
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
}

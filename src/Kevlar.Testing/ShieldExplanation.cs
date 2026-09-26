using System.Globalization;
using System.Text;

namespace Kevlar.Testing;

/// <summary>A read-only explanation of theoretical pipeline behavior, for diagnostics and tests.</summary>
/// <remarks>
/// Bounds count invocations of the protected operation through the described pipeline, excluding
/// fallback delegates, notification callbacks, and arbitrary work performed by user code.
/// Actual attempts depend on outcomes, cancellation, rejections, and timeouts. No predicates,
/// generators, or protected operations are invoked while explaining a descriptor.
/// </remarks>
public sealed class ShieldExplanation
{
    private ShieldExplanation(AttemptBoundKind attemptBoundKind, long? maximumAttempts, IReadOnlyList<StrategyExplanation> strategies)
    {
        AttemptBoundKind = attemptBoundKind;
        MaxAttempts = maximumAttempts;
        Strategies = strategies;
    }

    /// <summary>Whether the theoretical attempt bound is finite, unbounded, indeterminate, or overflows.</summary>
    public AttemptBoundKind AttemptBoundKind { get; }
    /// <summary>A conservative upper bound, available only when <see cref="AttemptBoundKind"/> is finite.</summary>
    public long? MaxAttempts { get; }
    /// <summary>Effective behavior in execution order, outermost first.</summary>
    public IReadOnlyList<StrategyExplanation> Strategies { get; }

    internal static ShieldExplanation Create(ShieldDescriptor descriptor)
    {
        var strategies = new StrategyExplanation[descriptor.Strategies.Count];
        var enclosingAttempts = new List<int>();
        var customScope = false;
        var indeterminate = false;
        var unbounded = false;
        var overflow = false;
        long maximumAttempts = 1;

        for (var index = 0; index < strategies.Length; index++)
        {
            var strategy = descriptor.Strategies[index];
            var scope = GetTimeoutScope(strategy, customScope, enclosingAttempts.Count);
            var handlingSource = GetHandlingSource(strategy);
            strategies[index] = new StrategyExplanation(index, strategy, handlingSource,
                DescribeHandling(strategy, handlingSource), scope, Array.AsReadOnly(enclosingAttempts.ToArray()));

            long factor = 1;
            switch (strategy)
            {
                case RetryStrategyDescriptor retry:
                    unbounded |= retry.MaxRetries == int.MaxValue;
                    factor = (long)retry.MaxRetries + 1;
                    if (retry.MaxRetries > 0) { enclosingAttempts.Add(index); }
                    break;
                case HedgeStrategyDescriptor hedge:
                    factor = (long)hedge.MaxHedgedAttempts + 1;
                    indeterminate |= hedge.HasActionGenerator && hedge.MaxHedgedAttempts > 0;
                    if (hedge.MaxHedgedAttempts > 0) { enclosingAttempts.Add(index); }
                    break;
                case CustomStrategyDescriptor { Kind: StrategyKind.Custom }:
                    indeterminate = true;
                    customScope = true;
                    break;
            }

            if (!overflow)
            {
                if (maximumAttempts > long.MaxValue / factor) { overflow = true; }
                else { maximumAttempts *= factor; }
            }
        }

        var kind = GetBoundKind(indeterminate, unbounded, overflow);
        return new ShieldExplanation(kind, kind == AttemptBoundKind.Finite ? maximumAttempts : null,
            Array.AsReadOnly(strategies));
    }

    private static TimeoutScope GetTimeoutScope(StrategyDescriptor strategy, bool customScope, int enclosingCount)
    {
        if (strategy is not TimeoutStrategyDescriptor) { return TimeoutScope.None; }
        if (customScope) { return TimeoutScope.Indeterminate; }
        return enclosingCount == 0 ? TimeoutScope.Execution : TimeoutScope.Attempt;
    }

    private static AttemptBoundKind GetBoundKind(bool indeterminate, bool unbounded, bool overflow)
    {
        if (indeterminate) { return AttemptBoundKind.Indeterminate; }
        if (unbounded) { return AttemptBoundKind.Unbounded; }
        return overflow ? AttemptBoundKind.Overflow : AttemptBoundKind.Finite;
    }

    private static HandlingSource GetHandlingSource(StrategyDescriptor strategy)
    {
        if (strategy.HandlingClause is null) { return HandlingSource.None; }
        if (strategy is CustomStrategyDescriptor) { return HandlingSource.Custom; }
        var hasOverride = strategy switch
        {
            RetryStrategyDescriptor retry => retry.HasHandlingOverride,
            HedgeStrategyDescriptor hedge => hedge.HasHandlingOverride,
            CircuitBreakerStrategyDescriptor breaker => breaker.HasHandlingOverride,
            FallbackStrategyDescriptor fallback => fallback.HasHandlingOverride,
            _ => false,
        };
        if (hasOverride) { return HandlingSource.LocalOverride; }
        return strategy.HandlingClause.Description is null ? HandlingSource.Default : HandlingSource.Ambient;
    }

    private static string? DescribeHandling(StrategyDescriptor strategy, HandlingSource source) => source switch
    {
        HandlingSource.None => null,
        HandlingSource.LocalOverride => "Local predicates replace ambient handling; predicate logic is opaque.",
        HandlingSource.Default when strategy is FallbackStrategyDescriptor =>
            "Ordinary errors and execution rejections; excludes cancellation and fatal exceptions.",
        HandlingSource.Default => "Ordinary errors; excludes cancellation, execution rejections, and fatal exceptions.",
        _ => strategy.HandlingClause?.Description ?? "Custom handling; predicate logic is opaque.",
    };

    /// <summary>Formats a human-readable explanation. Do not parse this output as a contract.</summary>
    public override string ToString()
    {
        var text = new StringBuilder("Theoretical protected-operation attempts: ");
        text.Append(AttemptBoundKind switch
        {
            AttemptBoundKind.Finite => $"at most {MaxAttempts!.Value.ToString(CultureInfo.InvariantCulture)}",
            AttemptBoundKind.Unbounded => "unbounded (RetryForever; no configured finite attempt budget)",
            AttemptBoundKind.Overflow => "finite product exceeds Int64.MaxValue",
            _ => "indeterminate (custom strategy or generated hedge action)",
        });
        text.AppendLine(". Actual attempts depend on outcomes, cancellation, rejections, and timeouts.");
        text.AppendLine("Order: outermost first. Counts exclude fallback delegates and user callbacks.");
        foreach (var step in Strategies)
        {
            text.Append(step.Index.ToString(CultureInfo.InvariantCulture)).Append(": ").AppendLine(step.Strategy.Description);
            if (step.HandlingDescription is { } handling)
            {
                text.Append("  Handling (").Append(step.HandlingSource).Append("): ").AppendLine(handling);
            }
            if (step.Strategy is TimeoutStrategyDescriptor timeout)
            {
                text.Append("  Timeout scope: ").Append(step.TimeoutScope)
                    .Append("; covers all downstream strategies");
                if (step.EnclosingAttemptStrategies.Count > 0)
                {
                    text.Append("; repeated by indexes ").Append(string.Join(", ", step.EnclosingAttemptStrategies));
                }
                if (timeout.HasTimeoutGenerator) { text.Append("; duration is dynamic and is not evaluated"); }
                text.AppendLine(". Cooperative cancellation, not a guaranteed wall-clock completion limit.");
            }
        }
        return text.ToString().TrimEnd();
    }
}

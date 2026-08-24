using System.Threading;

namespace Kevlar.Chaos.Internal;

internal abstract class ChaosStrategy : Strategy
{
    private const long GoldenRatio = unchecked((long)0x9E3779B97F4A7C15UL);
    private static long _nextUnseeded = DateTime.UtcNow.Ticks;

    private readonly bool _enabled;
    private readonly double _injectionRate;
    private readonly Func<KevlarContext, double>? _injectionRateGenerator;
    private readonly Func<KevlarContext, bool>? _enabledGenerator;
    private readonly Func<KevlarContext, bool>? _predicate;
    private readonly string? _requiredOperation;
    private readonly string? _requiredEnvironment;
    private readonly Action<ChaosEvent>? _onInjected;
    private long _randomState;

    protected override bool InvokesContinuationAtMostOnce => true;

    protected ChaosStrategy(ChaosOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        ValidateRate(options.InjectionRate, nameof(options.InjectionRate));

        _enabled = options.Enabled;
        _injectionRate = options.InjectionRate;
        _injectionRateGenerator = options.InjectionRateGenerator;
        _enabledGenerator = options.EnabledGenerator;
        _predicate = options.Predicate;
        _requiredOperation = options.Operation;
        _requiredEnvironment = options.Environment;
        _onInjected = options.OnInjected;
        _randomState = options.Seed is { } seed
            ? seed
            : Interlocked.Add(ref _nextUnseeded, GoldenRatio);
    }

    protected bool TryDecide(KevlarContext context, out ChaosDecision decision)
    {
        decision = default;
        if (!_enabled)
        {
            return false;
        }

        if (_enabledGenerator is not null && !_enabledGenerator(context))
        {
            return false;
        }

        if (_predicate is not null && !_predicate(context))
        {
            return false;
        }

        string? operation = null;
        string? environment = null;
        if (_requiredOperation is not null
            || _requiredEnvironment is not null
            || _onInjected is not null
            || ChaosMetrics.Enabled)
        {
            ChaosScope.Capture(out operation, out environment);
        }

        if (_requiredOperation is not null && !string.Equals(_requiredOperation, operation, StringComparison.Ordinal))
        {
            return false;
        }

        if (_requiredEnvironment is not null && !string.Equals(_requiredEnvironment, environment, StringComparison.Ordinal))
        {
            return false;
        }

        var rate = _injectionRateGenerator?.Invoke(context) ?? _injectionRate;
        ValidateRate(rate, "generated injection rate");
        if (rate <= 0)
        {
            return false;
        }

        var sample = rate >= 1 ? 0 : NextSample();
        if (sample >= rate)
        {
            return false;
        }

        decision = new ChaosDecision(operation, environment, rate, sample);
        return true;
    }

    protected void Notify(ChaosInjectionKind kind, KevlarContext context, ChaosDecision decision)
    {
        _onInjected?.Invoke(new ChaosEvent(
            kind,
            context,
            decision.Operation,
            decision.Environment,
            decision.Rate,
            decision.Sample));
        ChaosMetrics.Injection(kind, context.ShieldName, decision.Operation, decision.Environment);
    }

    private double NextSample()
    {
        var value = unchecked((ulong)Interlocked.Add(ref _randomState, GoldenRatio));
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1.0 / (1UL << 53));
    }

    private static void ValidateRate(double rate, string parameterName)
    {
        if (double.IsNaN(rate) || rate < 0 || rate > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, rate, "Injection rate must be between zero and one.");
        }
    }
}

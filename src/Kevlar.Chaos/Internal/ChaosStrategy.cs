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
    private readonly Func<ChaosEvent, ValueTask>? _onInjected;
    private readonly string _telemetryName;
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
        _telemetryName = options.Name ?? "Chaos";
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

    /// <summary>
    /// Records the injection and invokes <see cref="ChaosOptions.OnInjected"/>. The returned task
    /// completes when the callback does; callers must await it before injecting.
    /// </summary>
    protected ValueTask Notify(
        ChaosInjectionKind kind,
        KevlarContext context,
        ChaosDecision decision,
        Exception? exception = null)
    {
        ChaosMetrics.Injection(kind, context.ShieldName, decision.Operation, decision.Environment);
        context.RecordEvent(
            kind switch
            {
                ChaosInjectionKind.Latency => "chaos_latency",
                ChaosInjectionKind.Fault => "chaos_fault",
                ChaosInjectionKind.Outcome => "chaos_outcome",
                ChaosInjectionKind.Behavior => "chaos_behavior",
                _ => "chaos_injection",
            },
            KevlarTelemetrySeverity.Warning,
            exception,
            strategyName: _telemetryName);

        if (_onInjected is null)
        {
            return default;
        }

        ValueTask notification;
        try
        {
            notification = _onInjected(new ChaosEvent(
                kind,
                context,
                decision.Operation,
                decision.Environment,
                decision.Rate,
                decision.Sample));
        }
        catch (Exception callbackException)
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.Custom,
                context,
                callbackException,
                "ChaosOptions.OnInjected");
            return default;
        }

        if (notification.IsCompletedSuccessfully)
        {
            notification.GetAwaiter().GetResult();
            return default;
        }

        ThrowIfSynchronousExecutionCannotAwait(notification, context, "ChaosOptions.OnInjected");
        return AwaitNotificationAsync(notification, context);
    }

    /// <summary>
    /// Rejects a delegate that has not completed while the shield executes synchronously.
    /// Kevlar never blocks the calling thread on a callback.
    /// </summary>
    protected static void ThrowIfSynchronousExecutionCannotAwait(
        in ValueTask pending,
        KevlarContext context,
        string hookName)
    {
        if (pending.IsCompleted || !context.IsSynchronous)
        {
            return;
        }

        _ = pending.AsTask().ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        var shield = context.ShieldName is { Length: > 0 } name ? $" on shield '{name}'" : string.Empty;
        throw new NotSupportedException(
            $"Synchronous execution does not support {hookName} completing asynchronously{shield}. " +
            "Use ExecuteAsync instead of Execute, or make the callback complete synchronously.");
    }

    private static async ValueTask AwaitNotificationAsync(ValueTask notification, KevlarContext context)
    {
        try
        {
            await notification.ConfigureAwait(false);
        }
        catch (Exception callbackException)
        {
            KevlarDiagnostics.ReportCallbackError(
                CallbackErrorKind.Custom,
                context,
                callbackException,
                "ChaosOptions.OnInjected");
        }
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

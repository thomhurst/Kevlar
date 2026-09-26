using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kevlar.Analyzers;

/// <summary>Diagnoses literal duration budgets that cannot fit inside an enclosing timeout.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurationBudgetAnalyzer : DiagnosticAnalyzer
{
    // Match DelayHelper.MaximumDelay without loading the runtime library into the compiler.
    private const double MaximumDelayTicks = (uint.MaxValue - 1d) * TimeSpan.TicksPerMillisecond;

    /// <summary>The KEV015 rule.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KEV015",
        title: "Duration budget reaches or exceeds an outer timeout",
        messageFormat: "The configured {0} is greater than or equal to an outer timeout. Reduce it or increase the outer timeout; retry delay comparisons ignore jitter.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An inner timeout or the sum of configured retry delays cannot fit inside a literal outer timeout. Actual attempts depend on outcomes and cancellation; jitter is excluded from the delay comparison.",
        helpLinkUri: AnalyzerHelpLink.Create("KEV015", "duration-budgets"));

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var types = new KnownTypes(start.Compilation);
            start.RegisterOperationAction(operation => Analyze(operation, types), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, KnownTypes types)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!types.IsFluent(invocation.TargetMethod)
            || invocation.TargetMethod.Name is not ("Timeout" or "Retry"))
        {
            return;
        }

        var isTimeout = invocation.TargetMethod.Name == "Timeout";
        var innerTimeout = 0d;
        if (isTimeout && !TryTimeout(invocation, types, out innerTimeout))
        {
            return;
        }

        for (var receiver = Receiver(invocation); receiver is IInvocationOperation outer; receiver = Receiver(outer))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!types.IsFluent(outer.TargetMethod) || outer.TargetMethod.Name is "Wrap" or "Compose" or "Use")
            {
                break;
            }

            if (!TryTimeout(outer, types, out var budget))
            {
                continue;
            }

            var exceeds = isTimeout
                ? innerTimeout >= budget
                : RetryDelaysReachBudget(invocation, budget, types, context.CancellationToken);
            if (exceeds)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(),
                    isTimeout ? "inner timeout" : "retry delay sum"));
                return;
            }
        }
    }

    private static bool TryTimeout(IInvocationOperation invocation, KnownTypes types, out double ticks)
    {
        ticks = 0;
        return invocation.TargetMethod.Name == "Timeout"
            && TryDuration(Argument(invocation, "timeout"), types, out ticks)
            && ticks > 0 && ticks <= MaximumDelayTicks;
    }

    private static bool RetryDelaysReachBudget(
        IInvocationOperation retry, double budget, KnownTypes types, CancellationToken cancellationToken)
    {
        if (Argument(retry, "maxRetries")?.ConstantValue is not { HasValue: true, Value: int count }
            || count <= 0
            || Unwrap(Argument(retry, "backoff")) is not IInvocationOperation backoff
            || !SymbolEqualityComparer.Default.Equals(backoff.TargetMethod.ContainingType, types.Backoff))
        {
            return false;
        }

        var kind = backoff.TargetMethod.Name;
        var delayName = kind switch
        {
            "Constant" => "delay",
            "Linear" => "step",
            "Exponential" => "baseDelay",
            _ => null,
        };
        if (delayName is null || !TryDuration(Argument(backoff, delayName), types, out var baseTicks))
        {
            return false;
        }

        var cap = MaximumDelayTicks;
        var capArgument = Unwrap(Argument(backoff, "maxDelay"));
        if (capArgument is not null && capArgument.ConstantValue is not { HasValue: true, Value: null })
        {
            if (!TryDuration(capArgument, types, out cap) || cap > MaximumDelayTicks)
            {
                return false;
            }
        }

        var factor = 1d;
        if (kind == "Exponential"
            && (!TryNumber(Argument(backoff, "factor"), out factor) || factor < 1))
        {
            return false;
        }

        if (baseTicks == 0 || cap == 0 || (kind == "Constant" && baseTicks > MaximumDelayTicks))
        {
            return false;
        }

        double Delay(long attempt)
        {
            var ticks = kind switch
            {
                "Linear" => baseTicks * attempt,
                "Exponential" => baseTicks * Math.Pow(factor, attempt - 1),
                _ => baseTicks,
            };
            return Math.Floor(Math.Min(cap, ticks));
        }

        // Delays are nondecreasing before jitter. Endpoint sums bound each block, including
        // per-attempt tick truncation and caps. Refine only ambiguous bounds, with bounded
        // compiler work even for int.MaxValue retries or a factor extremely close to one.
        // An unresolved rounding boundary stays silent rather than producing a false positive.
        for (var blocks = 1; blocks <= 4096; blocks *= 2)
        {
            double lower = 0;
            double upper = 0;
            var width = ((long)count + blocks - 1) / blocks;
            for (long first = 1; first <= count; first += width)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var last = Math.Min(count, first + width - 1);
                lower += (last - first + 1) * Delay(first);
                upper += (last - first + 1) * Delay(last);
                if (lower >= budget)
                {
                    return true;
                }
            }

            if (upper < budget || width == 1)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryDuration(IOperation? operation, KnownTypes types, out double ticks)
    {
        ticks = 0;
        operation = Unwrap(operation);
        if (operation is IFieldReferenceOperation field && field.Field.Name == "Zero"
            && SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, types.TimeSpan))
        {
            return true;
        }

        if (operation is not IInvocationOperation factory
            || !SymbolEqualityComparer.Default.Equals(factory.TargetMethod.ContainingType, types.TimeSpan)
            || factory.Arguments.Length != 1
            || !TryNumber(factory.Arguments[0].Value, out var value))
        {
            return false;
        }

        var scale = factory.TargetMethod.Name switch
        {
            "FromDays" => (double)TimeSpan.TicksPerDay,
            "FromHours" => TimeSpan.TicksPerHour,
            "FromMinutes" => TimeSpan.TicksPerMinute,
            "FromSeconds" => TimeSpan.TicksPerSecond,
            "FromMilliseconds" => TimeSpan.TicksPerMillisecond,
            "FromMicroseconds" => 10,
            "FromTicks" => 1,
            _ => 0,
        };
        ticks = Math.Truncate(value * scale);
        return scale != 0 && value >= 0 && ticks < long.MaxValue && !double.IsInfinity(ticks);
    }

    private static bool TryNumber(IOperation? operation, out double value)
    {
        value = 0;
        if (operation?.ConstantValue is not { HasValue: true, Value: { } constant })
        {
            return false;
        }

        switch (constant)
        {
            case int number: value = number; break;
            case long number: value = number; break;
            case double number: value = number; break;
            default: return false;
        }

        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static IOperation? Argument(IInvocationOperation invocation, string name) =>
        invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == name)?.Value;

    private static IOperation? Receiver(IInvocationOperation invocation) => Unwrap(
        invocation.Instance ?? (invocation.TargetMethod.IsExtensionMethod
            ? invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value
            : null));

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { OperatorMethod: null } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private sealed class KnownTypes
    {
        private readonly ImmutableArray<INamedTypeSymbol?> _fluentTypes;
        internal INamedTypeSymbol? Backoff { get; }
        internal INamedTypeSymbol? TimeSpan { get; }

        internal KnownTypes(Compilation compilation)
        {
            _fluentTypes = ImmutableArray.Create(
                compilation.GetTypeByMetadataName("Kevlar.Shield"),
                compilation.GetTypeByMetadataName("Kevlar.Shield`1"),
                compilation.GetTypeByMetadataName("Kevlar.ShieldBuilder"),
                compilation.GetTypeByMetadataName("Kevlar.ShieldBuilder`1"),
                compilation.GetTypeByMetadataName("Kevlar.ShieldExtensions"));
            Backoff = compilation.GetTypeByMetadataName("Kevlar.Backoff");
            TimeSpan = compilation.GetTypeByMetadataName("System.TimeSpan");
        }

        internal bool IsFluent(IMethodSymbol method) => _fluentTypes.Any(type =>
            type is not null && SymbolEqualityComparer.Default.Equals(type, method.ContainingType.OriginalDefinition));
    }
}

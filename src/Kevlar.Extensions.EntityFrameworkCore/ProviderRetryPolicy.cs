using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kevlar.Extensions.EntityFrameworkCore;

internal sealed class ProviderRetryPolicy(
    ExecutionStrategy strategy,
    Func<Exception, bool> shouldRetry,
    Func<Exception, bool> shouldVerify,
    Func<Exception, TimeSpan?> nextDelay,
    List<Exception> exceptions)
{
    internal const string TrimmingMessage = "Default EF Core retry policy discovery reflects over the installed provider's execution strategy. Use an explicit Shield for trimmed applications.";

    internal bool ShouldVerifySuccess(Exception exception) => shouldVerify(exception);

    internal Shield CreateShield() => Shield.When(exception =>
        ExecutionStrategy.CallOnWrappedException(exception, shouldRetry)).Retry(options =>
    {
        options.MaxRetries = strategy.MaxRetryCount;
        options.Backoff = Backoff.None;
        options.DelayGenerator = retry =>
        {
            // Provider delay algorithms use the number of failures in the current execution.
            // Rebuild that count from the Kevlar event so verification has its own retry allowance.
            exceptions.Clear();
            for (var attempt = 0; attempt <= retry.AttemptNumber; attempt++)
            {
                exceptions.Add(retry.Exception!);
            }

            return new ValueTask<TimeSpan?>(nextDelay(retry.Exception!) ?? TimeSpan.Zero);
        };
    });

    [RequiresUnreferencedCode(TrimmingMessage)]
    internal static ProviderRetryPolicy Create(
        ExecutionStrategyDependencies dependencies,
        Func<ExecutionStrategyDependencies, IExecutionStrategy>? factory)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        var policy = factory?.Invoke(dependencies);
        if (policy is null)
        {
            var provider = dependencies.Options.Extensions.Single(extension => extension.Info.IsDatabaseProvider);
            var assembly = provider.GetType().Assembly;
            var typeName = assembly.GetName().Name switch
            {
                "Microsoft.EntityFrameworkCore.SqlServer" => "Microsoft.EntityFrameworkCore.SqlServerRetryingExecutionStrategy",
                "Npgsql.EntityFrameworkCore.PostgreSQL" => "Npgsql.EntityFrameworkCore.PostgreSQL.NpgsqlRetryingExecutionStrategy",
                _ => throw new NotSupportedException("This EF Core provider has no automatic Kevlar retry policy. Supply an explicit Shield.")
            };
            var type = assembly.GetType(typeName, throwOnError: true)!;
            policy = (IExecutionStrategy)Activator.CreateInstance(type, dependencies)!;
        }

        if (policy is not ExecutionStrategy strategy)
        {
            throw new NotSupportedException("The configured provider retry policy must derive from ExecutionStrategy. Supply an explicit Shield instead.");
        }

        var policyType = strategy.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var shouldRetry = policyType.GetMethod("ShouldRetryOn", flags)!
            .CreateDelegate<Func<Exception, bool>>(strategy);
        var shouldVerify = policyType.GetMethod("ShouldVerifySuccessOn", flags)!
            .CreateDelegate<Func<Exception, bool>>(strategy);
        var nextDelay = policyType.GetMethod("GetNextDelay", flags)!
            .CreateDelegate<Func<Exception, TimeSpan?>>(strategy);
        var exceptions = (List<Exception>)policyType.GetProperty("ExceptionsEncountered", flags)!.GetValue(strategy)!;
        return new ProviderRetryPolicy(strategy, shouldRetry, shouldVerify, nextDelay, exceptions);
    }
}

using System.Diagnostics.CodeAnalysis;
using Kevlar;
using Kevlar.Extensions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Configures relational EF Core providers to execute through Kevlar.</summary>
public static class KevlarDbContextOptionsBuilderExtensions
{
    /// <summary>Uses an explicit sequential shield for database operations and transaction verification.</summary>
    /// <param name="builder">Options builder with a relational provider already configured.</param>
    /// <param name="shield">The shield to reuse for each execution strategy instance.</param>
    /// <returns>The supplied builder.</returns>
    public static DbContextOptionsBuilder UseKevlarExecutionStrategy(this DbContextOptionsBuilder builder, Shield shield)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shield);
        var relational = RelationalOptionsExtension.Extract(builder.Options);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            relational.WithExecutionStrategyFactory(dependencies => new KevlarExecutionStrategy(dependencies, shield)));
        return builder;
    }

    /// <summary>Uses the configured provider retry policy, or SQL Server/Npgsql defaults, through Kevlar.</summary>
    /// <param name="builder">Options builder with a relational provider already configured.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>Configure provider retry options before this method to preserve their classification, count, and delays.</remarks>
    [RequiresUnreferencedCode(ProviderRetryPolicy.TrimmingMessage)]
    public static DbContextOptionsBuilder UseKevlarExecutionStrategy(this DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var relational = RelationalOptionsExtension.Extract(builder.Options);
        var factory = relational.ExecutionStrategyFactory;
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            relational.WithExecutionStrategyFactory(dependencies => KevlarExecutionStrategy.CreateDefault(dependencies, factory)));
        return builder;
    }

    /// <summary>Uses an explicit sequential shield while preserving the typed options builder.</summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="builder">Options builder with a relational provider already configured.</param>
    /// <param name="shield">The shield to reuse.</param>
    /// <returns>The supplied builder.</returns>
    public static DbContextOptionsBuilder<TContext> UseKevlarExecutionStrategy<TContext>(
        this DbContextOptionsBuilder<TContext> builder, Shield shield) where TContext : DbContext
    {
        UseKevlarExecutionStrategy((DbContextOptionsBuilder)builder, shield);
        return builder;
    }

    /// <summary>Uses the provider retry policy while preserving the typed options builder.</summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="builder">Options builder with a relational provider already configured.</param>
    /// <returns>The supplied builder.</returns>
    [RequiresUnreferencedCode(ProviderRetryPolicy.TrimmingMessage)]
    public static DbContextOptionsBuilder<TContext> UseKevlarExecutionStrategy<TContext>(
        this DbContextOptionsBuilder<TContext> builder) where TContext : DbContext
    {
        UseKevlarExecutionStrategy((DbContextOptionsBuilder)builder);
        return builder;
    }
}

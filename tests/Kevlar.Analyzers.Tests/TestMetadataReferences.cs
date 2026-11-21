using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Kevlar.Analyzers.Tests;

/// <summary>
/// Metadata references shared by every test compilation. Creating fresh references per test
/// defeats Roslyn's metadata and assembly-symbol reuse, so each parallel test loaded and bound
/// the whole platform again; on macOS arm64 the suite was killed before any test finished.
/// </summary>
internal static class TestMetadataReferences
{
    /// <summary>The test host's trusted platform assemblies plus the Kevlar core assembly.</summary>
    public static ImmutableArray<MetadataReference> Core { get; } =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Shield).Assembly.Location))
            .ToImmutableArray();

    /// <summary><see cref="Core"/> plus the Kevlar Chaos and rate-limiting assemblies.</summary>
    public static ImmutableArray<MetadataReference> WithExtensions { get; } =
        Core
            .Add(MetadataReference.CreateFromFile(typeof(Kevlar.Chaos.ChaosShield).Assembly.Location))
            .Add(MetadataReference.CreateFromFile(
                typeof(Kevlar.Extensions.RateLimiting.ShieldRateLimiterExtensions).Assembly.Location));
}

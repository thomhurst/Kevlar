namespace Kevlar.Testing;

/// <summary>Framework-independent assertions for structured shield descriptors.</summary>
public static class ShieldDescriptorAssertionExtensions
{
    /// <summary>Asserts at least one descriptor of <typeparamref name="TDescriptor"/> and returns the first.</summary>
    public static TDescriptor AssertContains<TDescriptor>(this ShieldDescriptor descriptor)
        where TDescriptor : StrategyDescriptor
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        foreach (var strategy in descriptor.Strategies)
        {
            if (strategy is TDescriptor typed)
            {
                return typed;
            }
        }

        throw new ShieldAssertionException(
            $"Expected at least one {typeof(TDescriptor).Name}, actual 0. " +
            $"Pipeline: [{Format(descriptor.Strategies.Select(static item => item.Kind))}].");
    }

    /// <summary>Asserts the exact strategy order and returns the descriptor for chaining.</summary>
    public static ShieldDescriptor AssertStrategyOrder(
        this ShieldDescriptor descriptor,
        params StrategyKind[] expected)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (expected is null)
        {
            throw new ArgumentNullException(nameof(expected));
        }

        if (descriptor.Strategies.Count == expected.Length)
        {
            var matches = true;
            for (var index = 0; index < expected.Length; index++)
            {
                matches &= descriptor.Strategies[index].Kind == expected[index];
            }

            if (matches)
            {
                return descriptor;
            }
        }

        throw new ShieldAssertionException(
            $"Shield strategy order mismatch: expected [{Format(expected)}], " +
            $"actual [{Format(descriptor.Strategies.Select(static item => item.Kind))}].");
    }

    /// <summary>Asserts the exact number of strategies and returns the descriptor for chaining.</summary>
    public static ShieldDescriptor AssertStrategyCount(this ShieldDescriptor descriptor, int expected)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (expected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expected), "Expected count must not be negative.");
        }

        if (descriptor.Strategies.Count != expected)
        {
            throw new ShieldAssertionException(
                $"Shield strategy count mismatch: expected {expected} strategies, " +
                $"actual {descriptor.Strategies.Count}.");
        }

        return descriptor;
    }

    /// <summary>Asserts exactly one descriptor of <typeparamref name="TDescriptor"/> and returns it.</summary>
    public static TDescriptor AssertContainsSingle<TDescriptor>(this ShieldDescriptor descriptor)
        where TDescriptor : StrategyDescriptor
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        TDescriptor? match = null;
        var count = 0;
        foreach (var strategy in descriptor.Strategies)
        {
            if (strategy is TDescriptor typed)
            {
                match = typed;
                count++;
            }
        }

        if (count != 1)
        {
            throw new ShieldAssertionException(
                $"Expected exactly one {typeof(TDescriptor).Name}, actual {count}. " +
                $"Pipeline: [{Format(descriptor.Strategies.Select(static item => item.Kind))}].");
        }

        return match!;
    }

    private static string Format(IEnumerable<StrategyKind> kinds) => string.Join(", ", kinds);
}

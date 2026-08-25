namespace Kevlar.Internal;

internal static class ShieldDecoration
{
    public static IShieldDecorator[] MergeForComposition(
        IShieldDecorator[] first,
        bool firstHasStrategies,
        IShieldDecorator[] second)
    {
        return firstHasStrategies ? first : Union(first, second);
    }

    public static bool HasResilienceStrategies(Strategy[] strategies) =>
        strategies.Any(static strategy => strategy is not ITransparentStrategy);

    private static IShieldDecorator[] Union(
        IShieldDecorator[] first,
        IShieldDecorator[] second)
    {
        if (first.Length == 0)
        {
            return second;
        }

        if (second.Length == 0)
        {
            return first;
        }

        var union = new IShieldDecorator[first.Length + second.Length];
        Array.Copy(first, union, first.Length);
        var count = first.Length;
        foreach (var decorator in second)
        {
            if (!IsApplied(union, count, decorator))
            {
                union[count++] = decorator;
            }
        }

        if (count != union.Length)
        {
            Array.Resize(ref union, count);
        }

        return union;
    }

    public static Shield Apply(
        Shield shield,
        string? name,
        IEnumerable<IShieldDecorator> decorators)
    {
        foreach (var decorator in decorators)
        {
            if (IsApplied(shield.AppliedDecorators, decorator))
            {
                continue;
            }

            var appliedDecorators = shield.AppliedDecorators;
            shield = decorator.Decorate(shield, name)
                ?? throw new InvalidOperationException("A shield decorator returned null.");
            shield = shield.MarkDecoratorApplied(appliedDecorators, decorator);
        }

        return shield;
    }

    public static Shield<TResult> Apply<TResult>(
        Shield<TResult> shield,
        string? name,
        IEnumerable<IShieldDecorator> decorators)
    {
        foreach (var decorator in decorators)
        {
            if (IsApplied(shield.AppliedDecorators, decorator))
            {
                continue;
            }

            var appliedDecorators = shield.AppliedDecorators;
            shield = decorator.Decorate(shield, name)
                ?? throw new InvalidOperationException("A shield decorator returned null.");
            shield = shield.MarkDecoratorApplied(appliedDecorators, decorator);
        }

        return shield;
    }

    private static bool IsApplied(
        IShieldDecorator[] appliedDecorators,
        IShieldDecorator decorator)
        => IsApplied(appliedDecorators, appliedDecorators.Length, decorator);

    private static bool IsApplied(
        IShieldDecorator[] appliedDecorators,
        int count,
        IShieldDecorator decorator)
    {
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(appliedDecorators[i], decorator))
            {
                return true;
            }
        }

        return false;
    }
}

namespace Kevlar.Internal;

internal static class ShieldDecoration
{
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
    {
        foreach (var applied in appliedDecorators)
        {
            if (ReferenceEquals(applied, decorator))
            {
                return true;
            }
        }

        return false;
    }
}

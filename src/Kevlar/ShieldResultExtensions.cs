using Kevlar.Internal;

namespace Kevlar;

/// <summary>
/// Result-handling clauses for <see cref="Shield{TResult}"/> chains whose result is a reference
/// type. They are extension methods because only an extension method can constrain
/// <c>TResult</c>: a method declared on <see cref="Shield{TResult}"/> itself inherits whatever
/// the shield was built for, so a null clause written there would compile for <c>int</c> too.
/// </summary>
public static class ShieldResultExtensions
{
    /// <summary>The clause term rendered for a null-result check, e.g. <c>[when null result]</c>.</summary>
    private const string NullResultTerm = "null result";

    /// <summary>
    /// Starts a handling clause for <see langword="null"/> results. Use
    /// <see cref="Shield{TResult}.WhenAnyError"/> to return to default handling.
    /// </summary>
    /// <remarks>
    /// Prefer this to <see cref="Shield{TResult}.WhenResultIsDefault"/> whenever the result is a
    /// reference type: both match the same values, but this one cannot be written for a value type
    /// whose <c>default</c> — <c>0</c>, <see langword="false"/> — is usually a legitimate result
    /// rather than a failure.
    /// </remarks>
    public static ShieldBuilder<TResult> WhenResultIsNull<TResult>(this Shield<TResult> shield)
        where TResult : class?
    {
        Throw.IfNull(shield, nameof(shield));
        return new ShieldBuilder<TResult>(shield).WithDefaultResult(NullResultTerm);
    }

    /// <summary>Returns a new builder that also handles <see langword="null"/> results.</summary>
    /// <remarks>
    /// Prefer this to <see cref="ShieldBuilder{TResult}.OrResultIsDefault"/> whenever the result is
    /// a reference type; see <see cref="WhenResultIsNull{TResult}(Shield{TResult})"/>.
    /// </remarks>
    public static ShieldBuilder<TResult> OrResultIsNull<TResult>(this ShieldBuilder<TResult> builder)
        where TResult : class?
    {
        Throw.IfNull(builder, nameof(builder));
        return builder.WithDefaultResult(NullResultTerm);
    }
}

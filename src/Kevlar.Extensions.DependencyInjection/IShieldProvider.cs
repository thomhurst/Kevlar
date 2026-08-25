namespace Kevlar.Extensions.DependencyInjection;

/// <summary>Provides the current immutable snapshot of a named shield.</summary>
/// <remarks>
/// Read <see cref="Current"/> once for each operation, then execute that snapshot. The read is
/// atomic and allocation-free. Providers registered by <c>AddReloadingShield</c> may publish a
/// replacement; ordinary shield providers always return their original snapshot.
/// </remarks>
public interface IShieldProvider
{
    /// <summary>Gets the current last known-good shield snapshot.</summary>
    Shield Current { get; }
}

/// <summary>Provides the current immutable snapshot of a named result-aware shield.</summary>
/// <typeparam name="TResult">The shield result type.</typeparam>
/// <remarks>
/// Read <see cref="Current"/> once for each operation, then execute that snapshot. The read is
/// atomic and allocation-free. Providers registered by <c>AddReloadingShield&lt;TResult&gt;</c>
/// may publish a replacement; ordinary shield providers always return their original snapshot.
/// </remarks>
public interface IShieldProvider<TResult>
{
    /// <summary>Gets the current last known-good shield snapshot.</summary>
    Shield<TResult> Current { get; }
}

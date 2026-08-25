namespace Kevlar.Testing;

/// <summary>Read-only handling-clause metadata.</summary>
public sealed class HandlingClauseDescriptor
{
    internal HandlingClauseDescriptor(string? description, bool isContextAware)
    {
        Description = description;
        IsContextAware = isContextAware;
    }

    /// <summary>The clause text used in pipeline descriptions, when explicitly configured.</summary>
    public string? Description { get; }

    /// <summary>Whether the clause consults execution or strategy context.</summary>
    public bool IsContextAware { get; }
}

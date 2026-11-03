namespace Ananke.Abstractions.Agents;

/// <summary>Specification for a child cell to be created during division.</summary>
public sealed record ChildSpec
{
    /// <summary>Name for the new child cell.</summary>
    public required string Name { get; init; }

    /// <summary>Primary domain this child will serve.</summary>
    public required string Domain { get; init; }

    /// <summary>Tool names assigned to this child.</summary>
    public required IReadOnlyList<string> Tools { get; init; }

    /// <summary>Job names assigned to this child.</summary>
    public required IReadOnlyList<string> Jobs { get; init; }

    /// <summary>Optional system prompt override for this child's agent jobs.</summary>
    public string? SystemPromptOverride { get; init; }
}

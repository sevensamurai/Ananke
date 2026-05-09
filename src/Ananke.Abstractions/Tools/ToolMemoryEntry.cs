namespace Ananke.Abstractions.Tools;

/// <summary>
/// An entry in the semantic tool index. Tracks identity, health, and usage
/// metadata for a single <c>ToolDefinition</c>.
/// </summary>
public sealed record ToolMemoryEntry
{
    /// <summary>The tool's canonical name (matches <c>ToolDefinition.Name</c>).</summary>
    public required string ToolName { get; init; }

    /// <summary>The name of the <c>ToolKit</c> that owns this tool.</summary>
    public required string KitName { get; init; }

    /// <summary>Human-readable description forwarded from <c>ToolDefinition.Description</c>.</summary>
    public required string Description { get; init; }

    /// <summary>Semantic tags copied from <c>ToolDefinition.Tags</c> for pre-filter narrowing.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Current operational health of this tool.</summary>
    public ToolHealth Health { get; init; } = ToolHealth.Healthy;

    /// <summary>Total number of successful recalls (gate selections) since registration.</summary>
    public int HitCount { get; init; }

    /// <summary>Timestamp of the last recall; <see cref="DateTimeOffset.MinValue"/> if never recalled.</summary>
    public DateTimeOffset LastUsed { get; init; } = DateTimeOffset.MinValue;
}

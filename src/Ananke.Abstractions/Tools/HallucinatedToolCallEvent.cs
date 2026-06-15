namespace Ananke.Abstractions.Tools;

/// <summary>
/// Emitted when the model requests a tool that is not registered in the ToolKit.
/// </summary>
public sealed record HallucinatedToolCallEvent
{
    public required string RequestedToolName { get; init; }
    public required string? RequestedKitName { get; init; }
    public required string AgentId { get; init; }
    public required string EpisodeId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

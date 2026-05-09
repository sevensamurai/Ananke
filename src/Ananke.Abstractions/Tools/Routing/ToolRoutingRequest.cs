namespace Ananke.Abstractions.Tools.Routing;

/// <summary>
/// Input passed to an <see cref="ISmartToolRouter"/> stage.
/// </summary>
public sealed record ToolRoutingRequest
{
    /// <summary>The latest user-turn text used as the routing query.</summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// Tool candidates fed into this stage. For the first stage in a chain
    /// this is the full kit; for later stages it is the previous stage's
    /// <see cref="ToolRoutingDecision.SelectedTools"/>.
    /// </summary>
    public required IReadOnlyList<ToolMemoryEntry> Candidates { get; init; }

    /// <summary>
    /// Optional summary of recent turns. Stages may ignore this.
    /// </summary>
    public IReadOnlyList<string>? ConversationDigest { get; init; }

    /// <summary>Soft cap on the number of selected tools. Default 8.</summary>
    public int MaxSelected { get; init; } = 8;

    /// <summary>
    /// Tools that must always be included in the final selection, regardless of
    /// <see cref="MaxSelected"/>. The composite router preserves these first and
    /// fills the remaining slots from the stage-selected candidates.
    /// </summary>
    public IReadOnlyList<ToolMemoryEntry> PinnedTools { get; init; } = [];
}

namespace Ananke.Design.Tools;

/// <summary>
/// Discriminated descriptor for a single stage in a declarative router chain
/// The <see cref="Kind"/> field selects the concrete stage type;
/// each subclass carries the configuration specific to that stage.
/// </summary>
public abstract record RouterStageDescriptor
{
    /// <summary>
    /// Stage kind discriminator as written in the manifest
    /// (e.g. <c>"pinned"</c>, <c>"health_filter"</c>, <c>"semantic_recall"</c>,
    /// <c>"affinity_rerank"</c>, <c>"heuristic_tags"</c>, <c>"llm"</c>).
    /// </summary>
    public required string Kind { get; init; }
}

/// <summary>
/// Always-on tool names placed at the front of the selection window.
/// <c>kind: pinned</c>
/// </summary>
public sealed record PinnedStageDescriptor : RouterStageDescriptor
{
    /// <summary>Tool names that must always appear in the window.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];
}

/// <summary>
/// Drops tools whose health is <c>Offline</c> or <c>Cooldown</c>.
/// <c>kind: health_filter</c>
/// </summary>
public sealed record HealthFilterStageDescriptor : RouterStageDescriptor;

/// <summary>
/// Semantic recall from <c>IToolMemory</c>.
/// <c>kind: semantic_recall</c>
/// </summary>
public sealed record SemanticRecallStageDescriptor : RouterStageDescriptor
{
    /// <summary>Maximum entries to recall per turn. Defaults to 8.</summary>
    public int TopK { get; init; } = 8;
}

/// <summary>
/// Re-ranks candidates by UCB affinity score.
/// <c>kind: affinity_rerank</c>
/// </summary>
public sealed record AffinityRerankStageDescriptor : RouterStageDescriptor;

/// <summary>
/// Keeps candidates whose tags match a heuristic derived from the user message.
/// The heuristic splits the message into tokens and checks tag overlap.
/// <c>kind: heuristic_tags</c>
/// </summary>
public sealed record HeuristicTagsStageDescriptor : RouterStageDescriptor;

/// <summary>
/// Delegates routing to a cheap LLM.
/// <c>kind: llm</c>
/// </summary>
public sealed record LlmStageDescriptor : RouterStageDescriptor
{
    /// <summary>
    /// Key referencing a model alias in the manifest <c>models:</c> section.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>Soft cap on selected tools. Defaults to 8.</summary>
    public int MaxSelected { get; init; } = 8;
}

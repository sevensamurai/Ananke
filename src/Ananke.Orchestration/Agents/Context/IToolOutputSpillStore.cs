namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Persists an oversized tool result outside the context window and hands back a locator the
/// model can use to read it.
/// </summary>
/// <remarks>
/// The seam exists so that bounding a tool result never means <em>losing</em> it. A store is
/// optional: with none configured an oversized result is truncated instead, which bounds the
/// window but does discard the tail. Implementations must tolerate concurrent calls.
/// </remarks>
public interface IToolOutputSpillStore
{
    /// <summary>Persists <paramref name="content"/> verbatim and returns how to reach it again.</summary>
    /// <param name="toolName">
    /// The tool that produced the output. A <em>hint</em> used to make the stored item
    /// recognisable — never a path, and implementations must not treat it as one.
    /// </param>
    /// <param name="content">The full, unmodified tool output.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SpilledToolOutput> SaveAsync(string toolName, string content, CancellationToken ct = default);
}

/// <summary>Where a spilled tool result went, and how to read it.</summary>
public sealed record SpilledToolOutput
{
    /// <summary>An opaque handle the retrieval hint explains how to use.</summary>
    public required string Locator { get; init; }

    /// <summary>
    /// One line telling the model how to read the full output. Backend-supplied, because only the
    /// backend knows what reaching its storage actually takes.
    /// </summary>
    public required string RetrievalHint { get; init; }

    /// <summary>Exact size of what was stored.</summary>
    public required long Bytes { get; init; }
}

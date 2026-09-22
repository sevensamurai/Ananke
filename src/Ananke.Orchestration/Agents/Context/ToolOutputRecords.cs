namespace Ananke.Orchestration.Agents.Context;

/// <summary>What happened to one oversized tool result at the moment it arrived.</summary>
/// <remarks>
/// Reported when it happens rather than folded into the next assembly record, because it <em>is</em>
/// a distinct event with its own moment — the point at which a payload was stopped from entering
/// the window at all.
/// </remarks>
public sealed record ToolOutputCapRecord
{
    /// <summary>The tool whose result exceeded the cap.</summary>
    public required string ToolName { get; init; }

    /// <summary>Size of the result the tool actually returned.</summary>
    public required int CharsBefore { get; init; }

    /// <summary>Size of what went into the window instead.</summary>
    public required int CharsAfter { get; init; }

    /// <summary>
    /// Where the full output was persisted, or <see langword="null"/> when no spill store was
    /// configured and the tail was discarded.
    /// </summary>
    public SpilledToolOutput? Spilled { get; init; }

    /// <summary>Whether the full output survives somewhere.</summary>
    public bool Recoverable => Spilled is not null;
}

/// <summary>What pruning reclaimed from tool results already in the transcript.</summary>
public sealed record ToolOutputPruningRecord
{
    /// <summary>How many tool results were reduced.</summary>
    public required int ReplacedCount { get; init; }

    /// <summary>Total size of those results before reduction.</summary>
    public required int CharsBefore { get; init; }

    /// <summary>Total size after.</summary>
    public required int CharsAfter { get; init; }

    /// <summary>
    /// Whether pruning alone brought the request under budget, so the strategy never ran — and the
    /// transcript reached the model unrewritten rather than summarized.
    /// </summary>
    /// <remarks>
    /// Worth counting because it is a <em>fidelity</em> measure, not a spend one: it says how often
    /// the window was kept honest by discarding something stale instead of by paraphrasing
    /// everything that remained.
    /// </remarks>
    public required bool AvertedCompaction { get; init; }

    /// <summary>How much was reclaimed.</summary>
    public int CharsReclaimed => CharsBefore - CharsAfter;
}

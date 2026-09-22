using System.Text;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// The goal a job was given, the criteria that decide whether it met it, and the constraints that
/// hold for the whole of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A contract is not a message.</b> It is authored above the work — by whoever handed the job
/// down — and rendered into <em>every</em> assembly the job makes, so it cannot be evicted,
/// paraphrased or reordered by compaction. That is the whole mechanism, and it is deliberately not
/// a flag on a message: marking a message as pinned would put the obligation on every context
/// strategy, including ones written outside this repo, and a strategy that forgot would fail
/// silently and late.
/// </para>
/// <para>
/// <b>Why this is not just "put it in the system prompt".</b> It ends up adjacent to one, but a
/// system prompt says who the agent is and is written by whoever built the agent; a contract says
/// what this particular work item is for and is written by whoever handed it down. Keeping them
/// separate is what lets a caller supply a goal without rewriting a persona — and it is the seam a
/// parent will hand a child's contract through. It is also structured rather than prose, so what
/// counts as "done" is a list something can later read, not a paragraph.
/// </para>
/// <para>
/// Opt-in. A job with no contract behaves exactly as it does today.
/// </para>
/// </remarks>
public sealed record AgentContract
{
    /// <summary>What this work item is for. Required — a contract with no goal is not one.</summary>
    public required string Goal { get; init; }

    /// <summary>
    /// What must hold before the work is complete. Stated as separate items rather than a
    /// paragraph because they are checked separately.
    /// </summary>
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    /// <summary>
    /// What holds for the whole of this work item. These are stated once and never restated, which
    /// is exactly why they need to survive compaction.
    /// </summary>
    public IReadOnlyList<string> Constraints { get; init; } = [];

    /// <summary>
    /// Conditions that <em>rank</em> the work rather than deciding whether it is done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept apart from <see cref="AcceptanceCriteria"/> because the two must never be averaged. A
    /// failed acceptance criterion is not purchasable with a strong showing here — blending them is
    /// exactly the shape that lets a regression be bought back by an improvement somewhere else.
    /// </para>
    /// <para>
    /// So: acceptance criteria gate, quality criteria rank, and ranking only ever happens once every
    /// gate has passed.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> QualityCriteria { get; init; } = [];

    /// <summary>
    /// Two contracts are equal when they say the same thing.
    /// </summary>
    /// <remarks>
    /// A record's generated equality compares its list properties by reference, so two contracts
    /// built separately from identical text would not be equal — which is wrong for a value object,
    /// and quietly wrong in the way that matters: anything asking "has this work item been re-issued
    /// with different terms?" would answer yes every time.
    /// </remarks>
    public bool Equals(AgentContract? other) =>
        other is not null
        && string.Equals(Goal, other.Goal, StringComparison.Ordinal)
        && AcceptanceCriteria.SequenceEqual(other.AcceptanceCriteria, StringComparer.Ordinal)
        && QualityCriteria.SequenceEqual(other.QualityCriteria, StringComparer.Ordinal)
        && Constraints.SequenceEqual(other.Constraints, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Goal, StringComparer.Ordinal);
        foreach (var criterion in AcceptanceCriteria)
            hash.Add(criterion, StringComparer.Ordinal);
        foreach (var quality in QualityCriteria)
            hash.Add(quality, StringComparer.Ordinal);
        foreach (var constraint in Constraints)
            hash.Add(constraint, StringComparer.Ordinal);

        return hash.ToHashCode();
    }

    /// <summary>Throws if the contract is empty of substance. Called by the builders.</summary>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Goal))
            throw new ArgumentException("A contract must state a goal.", nameof(Goal));
    }

    /// <summary>Renders the contract as the text an assembly carries.</summary>
    /// <remarks>
    /// The wording tells the model these are <em>standing</em> terms rather than a fresh
    /// instruction, because a constraint stated once at turn one and re-rendered at turn fifty
    /// reads as a repeated demand otherwise.
    /// </remarks>
    internal string Render()
    {
        var text = new StringBuilder("# Contract\n\nGoal: ").Append(Goal.Trim());

        if (AcceptanceCriteria.Count > 0)
        {
            text.Append("\n\nAcceptance criteria — all of these must hold before the work is complete:");
            for (var i = 0; i < AcceptanceCriteria.Count; i++)
                text.Append("\n").Append(i + 1).Append(". ").Append(AcceptanceCriteria[i].Trim());
        }

        if (QualityCriteria.Count > 0)
        {
            text.Append("\n\nQuality criteria — these rank the work; they do not decide whether it is ")
                .Append("done, and a strong showing here never substitutes for an acceptance criterion:");
            foreach (var quality in QualityCriteria)
                text.Append("\n- ").Append(quality.Trim());
        }

        if (Constraints.Count > 0)
        {
            text.Append("\n\nStanding constraints — these hold for the whole of this work item and are ")
                .Append("not restated as the conversation goes on:");
            foreach (var constraint in Constraints)
                text.Append("\n- ").Append(constraint.Trim());
        }

        return text.ToString();
    }

    /// <summary>
    /// Combines an agent's system prompt with a contract, in that order: who you are, then what you
    /// were given.
    /// </summary>
    internal static string? Compose(string? systemPrompt, AgentContract? contract)
    {
        if (contract is null)
            return systemPrompt;

        var rendered = contract.Render();
        return string.IsNullOrWhiteSpace(systemPrompt) ? rendered : systemPrompt + "\n\n" + rendered;
    }
}

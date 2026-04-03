namespace Ananke.Skills;

/// <summary>
/// Persists local up/down votes for skills. Scores influence catalog search ranking
/// and determine whether a skill is offered to agents (negative scores are filtered out).
/// </summary>
public interface ISkillScoreStore
{
    /// <summary>Records an up or down vote for the given skill.</summary>
    Task RecordVoteAsync(string skillId, VoteDirection direction, CancellationToken ct = default);

    /// <summary>Returns the current score for a skill. Returns a zero score if no votes exist.</summary>
    Task<SkillScore> GetScoreAsync(string skillId, CancellationToken ct = default);

    /// <summary>Returns scores for all voted skills.</summary>
    Task<IReadOnlyDictionary<string, SkillScore>> GetAllScoresAsync(CancellationToken ct = default);
}

namespace Ananke.Skills;

/// <summary>
/// Local scoring for a skill — tracks reliability via up/down votes.
/// Scores are deployment-local and influence catalog search ranking.
/// </summary>
public sealed record SkillScore(int UpVotes = 0, int DownVotes = 0)
{
    /// <summary>Net score (positive = reliable, negative = unreliable).</summary>
    public int Net => UpVotes - DownVotes;
}

/// <summary>Direction of a skill vote.</summary>
public enum VoteDirection
{
    Up,
    Down
}

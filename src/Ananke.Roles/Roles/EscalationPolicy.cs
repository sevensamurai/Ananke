namespace Ananke.Roles.Roles;

/// <summary>
/// Thresholds that trigger escalation to a secondary model or reviewer lane.
/// </summary>
public sealed record EscalationPolicy
{
    /// <summary>
    /// Escalate after this many consecutive failures. <see langword="null"/> disables the threshold.
    /// </summary>
    public int? FailureCountThreshold { get; init; }

    /// <summary>
    /// Escalate after the prompt reaches this token count. <see langword="null"/> disables the threshold.
    /// </summary>
    public int? PromptTokenThreshold { get; init; }
}

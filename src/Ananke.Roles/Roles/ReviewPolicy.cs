namespace Ananke.Roles.Roles;

/// <summary>
/// Review requirements attached to a role.
/// </summary>
public sealed record ReviewPolicy
{
    /// <summary>
    /// When <see langword="true"/>, an LLM performs the first-pass review before any human escalation.
    /// </summary>
    public bool LlmFirstPass { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the role requires a final escalation-stage approval.
    /// </summary>
    public bool FinalApprovalRequired { get; init; }

    /// <summary>
    /// Additional reviewer identifiers that should be consulted for this role.
    /// </summary>
    public IReadOnlyList<string> AdditionalReviewerUserIds { get; init; } = [];
}

namespace Ananke.Roles.Roles;

/// <summary>
/// Describes a reusable role/persona for a studio workflow.
/// </summary>
public sealed record AgentRole
{
    /// <summary>Unique role name.</summary>
    public required string Name { get; init; }

    /// <summary>Domain tags used for routing and manifest intent hints.</summary>
    public required IReadOnlyList<string> DomainTags { get; init; }

    /// <summary>Primary model alias used by the role.</summary>
    public required string ModelAlias { get; init; }

    /// <summary>Optional escalation model alias used when the escalation policy is triggered.</summary>
    public string? EscalationModelAlias { get; init; }

    /// <summary>Path to the system prompt file used when building a manifest for this role.</summary>
    public required string SystemPromptPath { get; init; }

    /// <summary>Tool names exposed to this role.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Preferred sampling temperature for the role.</summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>Maximum tool rounds allowed for the role.</summary>
    public int MaxToolRounds { get; init; } = 3;

    /// <summary>Review requirements for work produced by this role.</summary>
    public ReviewPolicy Review { get; init; } = new();

    /// <summary>Optional escalation thresholds for this role.</summary>
    public EscalationPolicy? Escalation { get; init; }
}

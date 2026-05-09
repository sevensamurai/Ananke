namespace Ananke.Abstractions.Tools;

/// <summary>
/// Operational health state of a tool tracked by <see cref="IToolMemory"/>.
/// Used by the tool gate to exclude unavailable tools and by
/// <c>SemanticToolGateMiddleware</c> to inject health advisories into the agent context
/// </summary>
public enum ToolHealth
{
    /// <summary>Tool is operating normally.</summary>
    Healthy,

    /// <summary>Tool is experiencing intermittent failures; usable with caution.</summary>
    Degraded,

    /// <summary>Tool is temporarily suspended after repeated failures; will recover after a decay period.</summary>
    Cooldown,

    /// <summary>Tool is permanently offline for this session; will not be injected into the gate window.</summary>
    Offline
}

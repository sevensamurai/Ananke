namespace LogEventsDemo;

/// <summary>
/// Defines one component of the simulated distributed system:
/// its name, infrastructure dependencies, base error rates, and log templates.
/// </summary>
internal sealed record ServiceDefinition
{
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required IReadOnlyList<string> InfraDependencies { get; init; }
    public required IReadOnlyList<string> UpstreamServices { get; init; }

    /// <summary>Base probability of a transient error per log tick (0–1).</summary>
    public required float BaseErrorRate { get; init; }

    /// <summary>Template messages for normal operation logs.</summary>
    public required IReadOnlyList<string> NormalMessages { get; init; }

    /// <summary>Template messages for transient error logs.</summary>
    public required IReadOnlyList<string> TransientErrorMessages { get; init; }
}

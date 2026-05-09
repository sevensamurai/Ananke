namespace OrganicKernelDemo;

/// <summary>Parsed command-line options for the demo.</summary>
sealed record DemoOptions
{
    /// <summary>Human-in-the-loop approval for division decisions.</summary>
    public bool Supervised { get; init; }

    /// <summary>Show YAML snapshots, complexity details, and landscape dumps.</summary>
    public bool Verbose { get; init; }

    /// <summary>Simulate division (derive manifests + routing but don't spawn/kill).</summary>
    public bool Simulate { get; init; }

    /// <summary>
    /// Build and export the colony topology report after division.
    /// Enabled by default; pass <c>--no-topology</c> to skip.
    /// </summary>
    public bool Topology { get; init; } = true;

    public static DemoOptions Parse(string[] args) => new()
    {
        Supervised = args.Any(a => a.Equals("--supervised", StringComparison.OrdinalIgnoreCase)),
        Verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("-v", StringComparison.OrdinalIgnoreCase)),
        Simulate = args.Any(a => a.Equals("--simulate", StringComparison.OrdinalIgnoreCase)),
        Topology = !args.Any(a => a.Equals("--no-topology", StringComparison.OrdinalIgnoreCase))
    };
}

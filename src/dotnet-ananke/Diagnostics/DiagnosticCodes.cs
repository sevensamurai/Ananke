namespace Ananke.Tool.Diagnostics;

/// <summary>
/// Classifies manifest and topology errors into stable, machine-readable codes.
/// Each code maps to a specific failure mode that agents can branch on.
/// </summary>
/// <remarks>
/// Codes follow the pattern <c>ANANKE_{CATEGORY}_{NUMBER}</c>:
/// <list type="table">
///   <listheader><term>Category</term><description>Scope</description></listheader>
///   <item><term>MANIFEST</term><description>YAML parse and structure errors</description></item>
///   <item><term>TOPO</term><description>Topology / graph validation errors</description></item>
///   <item><term>MODEL</term><description>Model alias resolution errors</description></item>
/// </list>
/// </remarks>
internal static class DiagnosticCodes
{
    // ── Manifest errors ──────────────────────────────────────────────

    /// <summary>YAML parse error — file is not valid manifest YAML.</summary>
    public const string ManifestParseError = "ANANKE_MANIFEST_001";

    /// <summary>Missing required <c>name:</c> field.</summary>
    public const string ManifestMissingName = "ANANKE_MANIFEST_002";

    // ── Topology errors ──────────────────────────────────────────────

    /// <summary>A job is unreachable — not connected from any other job.</summary>
    public const string UnreachableJob = "ANANKE_TOPO_001";

    /// <summary>No terminal connection — no path to <c>End</c>.</summary>
    public const string MissingTerminal = "ANANKE_TOPO_002";

    /// <summary>Fork without matching join — parallel branches diverge without merging.</summary>
    public const string ForkWithoutJoin = "ANANKE_TOPO_003";

    /// <summary>Undefined job referenced in a connection line.</summary>
    public const string UndefinedJob = "ANANKE_TOPO_004";

    /// <summary>General topology validation error not covered by a more specific code.</summary>
    public const string TopologyInvalid = "ANANKE_TOPO_099";

    // ── Model errors ─────────────────────────────────────────────────

    /// <summary>A job references a model alias not defined in the <c>models:</c> section.</summary>
    public const string UndefinedModelAlias = "ANANKE_MODEL_001";

    /// <summary>
    /// Attempts to classify an exception message from <see cref="Ananke.Design.WorkflowScaffold"/>
    /// or <see cref="Ananke.Design.WorkflowManifest"/> into a specific diagnostic code.
    /// Falls back to <see cref="TopologyInvalid"/> or <see cref="ManifestParseError"/>
    /// for unrecognized messages.
    /// </summary>
    public static Diagnostic FromException(Exception ex, string phase)
    {
        var msg = ex.Message;

        // WorkflowDefinition.Validate() messages
        if (msg.Contains("Unreachable job"))
            return new Diagnostic
            {
                Code = UnreachableJob,
                Message = msg,
                Hint = "Ensure every job has at least one incoming connection.",
                DocsRef = "nnke docs dsl-syntax"
            };

        if (msg.Contains("has no outgoing connection"))
            return new Diagnostic
            {
                Code = MissingTerminal,
                Message = msg,
                Hint = "Add a terminal connection with .Then(\"jobName\", End) or '-> End' in DSL.",
                DocsRef = "nnke docs dsl-syntax"
            };

        if (msg.Contains("not defined as a job") || msg.Contains("references an undefined job"))
            return new Diagnostic
            {
                Code = UndefinedJob,
                Message = msg,
                Hint = "Check that all job names in connections match a declared job.",
                DocsRef = "nnke docs dsl-syntax"
            };

        if (msg.Contains("Missing required field") && msg.Contains("name"))
            return new Diagnostic
            {
                Code = ManifestMissingName,
                Message = msg,
                Hint = "Add a 'name:' field at the top of the .ananke.yml file.",
                DocsRef = "nnke docs dsl-syntax"
            };

        // Default: use phase to determine category
        var isManifest = phase == "manifest";
        return new Diagnostic
        {
            Code = isManifest ? ManifestParseError : TopologyInvalid,
            Message = msg,
            Hint = isManifest
                ? "Check the .ananke.yml file syntax. Run: nnke docs dsl-syntax"
                : "Check the topology connections. Run: nnke docs dsl-syntax",
            DocsRef = "nnke docs dsl-syntax"
        };
    }
}

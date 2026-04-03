namespace Ananke.Skills;

/// <summary>
/// Lightweight metadata describing an external skill. Cheap to load, cache, and search.
/// Resolved into a <see cref="Ananke.Orchestration.Tools.ToolDefinition"/> on demand
/// via <see cref="ISkillCatalog.ResolveAsync"/>.
/// </summary>
public sealed record SkillDescriptor
{
    /// <summary>Unique identifier within the catalog (e.g. <c>"stveenli/airbnb"</c>).</summary>
    public required string Id { get; init; }

    /// <summary>CLI package / tool name (e.g. <c>"airbnb-search"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description of what the skill does.</summary>
    public required string Description { get; init; }

    /// <summary>Keywords for filtering and search ranking.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Project homepage or repository URL.</summary>
    public string? Homepage { get; init; }

    /// <summary>How to run the skill's binary.</summary>
    public SkillInstallMethod Install { get; init; } = SkillInstallMethod.Uvx;

    /// <summary>
    /// The package name passed to the runner (e.g. <c>"airbnb-search"</c> for <c>uvx airbnb-search</c>).
    /// Defaults to <see cref="Name"/> if not set.
    /// </summary>
    public string? InstallPackage { get; init; }

    /// <summary>Effective package name — <see cref="InstallPackage"/> if set, otherwise <see cref="Name"/>.</summary>
    public string EffectivePackage => InstallPackage ?? Name;

    /// <summary>Local reliability score. <c>null</c> when the skill has never been voted on.</summary>
    public SkillScore? Score { get; init; }

    /// <summary>
    /// CLI parameters the tool accepts. Each entry maps to a <c>--flag</c> unless marked as positional or flag-only.
    /// If empty, the catalog resolver will expose a single free-text <c>query</c> parameter.
    /// </summary>
    public IReadOnlyList<SkillParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Additional CLI arguments appended to every invocation (e.g. <c>"--json"</c>).
    /// Use this for output format flags or other fixed arguments the LLM should not control.
    /// </summary>
    public string? ExtraCliArgs { get; init; }
}

/// <summary>
/// Describes a CLI parameter accepted by a skill.
/// </summary>
/// <param name="Name">Parameter name (becomes the JSON property key and CLI flag).</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="IsRequired">Whether the parameter is required.</param>
/// <param name="IsPositional">When <c>true</c>, the value is passed as a positional argument instead of <c>--name value</c>.</param>
/// <param name="IsFlag">When <c>true</c>, emitted as <c>--name</c> with no value (boolean flag). Not exposed to the LLM.</param>
public sealed record SkillParameter(
    string Name,
    string Description,
    bool IsRequired = false,
    bool IsPositional = false,
    bool IsFlag = false);

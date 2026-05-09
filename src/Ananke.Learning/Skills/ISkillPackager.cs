using Ananke.Learning.Episodes;
using Ananke.Learning.Features;


using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Skills;

/// <summary>
/// Export and import contract for portable skill packages. The packager
/// streams entries through an <see cref="ISkillPackageWriter"/>, applying
/// quality gates on export and trust scaling on import.
/// </summary>
public interface ISkillPackager
{
    /// <summary>
    /// Exports entries from empirical memory through a streaming writer,
    /// applying quality gates to select high-value entries.
    /// </summary>
    /// <param name="options">Quality gates and package metadata.</param>
    /// <param name="memory">Source empirical memory to export from.</param>
    /// <param name="writer">Streaming writer to write entries to.</param>
    /// <param name="episodes">Optional episode store for exporting linked episodes.</param>
    /// <param name="tagImportances">Optional learned tag importance weights.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Summary of what was exported.</returns>
    Task<SkillExportResult> ExportAsync(
        SkillExportOptions options,
        IEmpiricalMemory memory,
        ISkillPackageWriter writer,
        IEpisodeStore? episodes = null,
        TagImportanceMap? tagImportances = null,
        CancellationToken ct = default);

    /// <summary>
    /// Imports entries from a streaming reader into empirical memory,
    /// applying trust scaling and merge semantics.
    /// </summary>
    /// <param name="reader">Streaming reader to read entries from.</param>
    /// <param name="memory">Target empirical memory to import into.</param>
    /// <param name="episodes">Optional episode store for importing episodes.</param>
    /// <param name="options">Trust scaling and merge mode options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Summary of what was imported.</returns>
    Task<SkillImportResult> ImportAsync(
        ISkillPackageReader reader,
        IEmpiricalMemory memory,
        IEpisodeStore? episodes = null,
        SkillImportOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>Quality gates and metadata for skill export.</summary>
public sealed record SkillExportOptions
{
    /// <summary>Human-readable skill name.</summary>
    public required string Name { get; init; }

    /// <summary>Domain this skill applies to.</summary>
    public required string Domain { get; init; }

    /// <summary>Semantic version string.</summary>
    public required string Version { get; init; }

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Filter by kind. When <see langword="null"/>, all kinds are exported.</summary>
    public EmpiricalKind? Kind { get; init; }

    /// <summary>Minimum strength threshold. Default: 0.3.</summary>
    public float MinStrength { get; init; } = 0.3f;

    /// <summary>Minimum confidence threshold. Default: 0.2.</summary>
    public float MinConfidence { get; init; } = 0.2f;

    /// <summary>Minimum observation count. Default: 2.</summary>
    public int MinObservations { get; init; } = 2;

    /// <summary>Whether to include linked episodes. Default: <see langword="true"/>.</summary>
    public bool IncludeEpisodes { get; init; } = true;

    /// <summary>
    /// When set, only entries with at least one of these tags are exported.
    /// When <see langword="null"/>, all entries (subject to other gates) are exported.
    /// </summary>
    public IReadOnlyList<string>? RequiredTags { get; init; }
}

/// <summary>Trust scaling and merge semantics for skill import.</summary>
public sealed record SkillImportOptions
{
    /// <summary>Merge mode. Default: <see cref="SkillImportMode.Merge"/>.</summary>
    public SkillImportMode Mode { get; init; } = SkillImportMode.Merge;

    /// <summary>
    /// Scale factor applied to imported entry strengths.
    /// Use &lt; 1.0 for reduced trust in foreign knowledge. Default: 1.0.
    /// </summary>
    public float StrengthScale { get; init; } = 1.0f;

    /// <summary>Optional prefix prepended to imported entry IDs.</summary>
    public string? IdPrefix { get; init; }

    /// <summary>Source tag applied to imported entries. Default: <c>"skill-import"</c>.</summary>
    public string EvidenceSource { get; init; } = "skill-import";
}

/// <summary>Controls how imported entries interact with existing memory.</summary>
public enum SkillImportMode
{
    /// <summary>Merge with existing entries via semantic dedup.</summary>
    Merge,

    /// <summary>Add only new entries; skip those whose ID already exists.</summary>
    AddOnly
}

/// <summary>Result of a skill export operation.</summary>
public sealed record SkillExportResult
{
    /// <summary>Number of entries that passed quality gates and were exported.</summary>
    public required int EntriesExported { get; init; }

    /// <summary>Number of linked episodes exported.</summary>
    public required int EpisodesExported { get; init; }
}

/// <summary>Result of a skill import operation.</summary>
public sealed record SkillImportResult
{
    /// <summary>New entries added to memory.</summary>
    public required int EntriesAdded { get; init; }

    /// <summary>Entries merged via semantic dedup with existing memory.</summary>
    public required int EntriesMerged { get; init; }

    /// <summary>Entries skipped (e.g. duplicates in <see cref="SkillImportMode.AddOnly"/>).</summary>
    public required int EntriesSkipped { get; init; }

    /// <summary>Episodes imported into the episode store.</summary>
    public required int EpisodesImported { get; init; }
}

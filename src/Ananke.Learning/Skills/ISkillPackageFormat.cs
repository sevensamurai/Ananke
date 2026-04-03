using Ananke.Learning.Episodes;
using Ananke.Learning.Features;

namespace Ananke.Learning.Skills;

/// <summary>
/// Header metadata for a skill package, written before streamed data.
/// Contains package identity, domain, version, and optional tag importance
/// weights. Does not include the entries or episodes themselves — those are
/// streamed separately via <see cref="ISkillPackageWriter"/>.
/// </summary>
public sealed record SkillPackageHeader
{
    /// <summary>Unique package identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable skill name (e.g. "connect4-strategy").</summary>
    public required string Name { get; init; }

    /// <summary>Domain this skill applies to (e.g. "connect4", "incident-response").</summary>
    public required string Domain { get; init; }

    /// <summary>Semantic version string.</summary>
    public required string Version { get; init; }

    /// <summary>Optional description of what this skill contains.</summary>
    public string? Description { get; init; }

    /// <summary>Optional learned feature weights for tag-based recall boosting.</summary>
    public TagImportanceMap? TagImportances { get; init; }
}

/// <summary>
/// Provenance and statistics for a skill package, written after all
/// entries and episodes have been streamed.
/// </summary>
public sealed record TrainingManifest
{
    /// <summary>Total empirical entries in the package.</summary>
    public required int TotalEntries { get; init; }

    /// <summary>Total episodes in the package.</summary>
    public required int TotalEpisodes { get; init; }

    /// <summary>Average terminal reward across included episodes.</summary>
    public float AverageReward { get; init; }

    /// <summary>When this package was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Total training duration.</summary>
    public TimeSpan TrainingDuration { get; init; }

    /// <summary>Arbitrary statistics (e.g. win rate, convergence step).</summary>
    public IReadOnlyDictionary<string, string> Statistics { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Training configuration used to produce this skill.</summary>
    public IReadOnlyDictionary<string, string> Configuration { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// Streaming serialization contract for skill packages. Implementations
/// provide an <see cref="ISkillPackageWriter"/> for export and an
/// <see cref="ISkillPackageReader"/> for import, enabling entry-by-entry
/// I/O without materializing the entire package in memory.
/// </summary>
public interface ISkillPackageFormat
{
    /// <summary>MIME content type (e.g. <c>"application/json"</c>).</summary>
    string ContentType { get; }

    /// <summary>Creates a streaming writer that serializes to <paramref name="output"/>.</summary>
    ISkillPackageWriter CreateWriter(Stream output);

    /// <summary>
    /// Opens a stream for reading. The returned reader provides access to
    /// the header, streamed entries, streamed episodes, and manifest.
    /// </summary>
    Task<ISkillPackageReader> CreateReaderAsync(Stream input, CancellationToken ct = default);
}

/// <summary>
/// Streaming writer for skill packages. Entries and episodes are written
/// one at a time, flushing to the underlying stream incrementally.
/// </summary>
/// <remarks>
/// Call order: <see cref="WriteHeaderAsync"/> → zero or more
/// <see cref="WriteEntryAsync"/> → zero or more
/// <see cref="WriteEpisodeAsync"/> → <see cref="CompleteAsync"/>.
/// </remarks>
public interface ISkillPackageWriter : IAsyncDisposable
{
    /// <summary>Writes the package header. Must be called first.</summary>
    Task WriteHeaderAsync(SkillPackageHeader header, CancellationToken ct = default);

    /// <summary>Writes a single empirical entry to the entries stream.</summary>
    Task WriteEntryAsync(EmpiricalEntry entry, CancellationToken ct = default);

    /// <summary>Writes a single episode to the episodes stream.</summary>
    Task WriteEpisodeAsync(Episode episode, CancellationToken ct = default);

    /// <summary>
    /// Writes the training manifest and finalizes the package. Must be called last.
    /// </summary>
    Task CompleteAsync(TrainingManifest manifest, CancellationToken ct = default);
}

/// <summary>
/// Streaming reader for skill packages. After reading the header, entries
/// and episodes are yielded one at a time via <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
public interface ISkillPackageReader : IAsyncDisposable
{
    /// <summary>Reads the package header.</summary>
    Task<SkillPackageHeader> ReadHeaderAsync(CancellationToken ct = default);

    /// <summary>Yields entries one at a time from the package.</summary>
    IAsyncEnumerable<EmpiricalEntry> ReadEntriesAsync(CancellationToken ct = default);

    /// <summary>Yields episodes one at a time from the package.</summary>
    IAsyncEnumerable<Episode> ReadEpisodesAsync(CancellationToken ct = default);

    /// <summary>Reads the training manifest. Call after consuming entries and episodes.</summary>
    Task<TrainingManifest> ReadManifestAsync(CancellationToken ct = default);
}

using Ananke.Learning.Episodes;
using Ananke.Learning.Features;

namespace Ananke.Learning.Skills;

/// <summary>
/// Default <see cref="ISkillPackager"/> that streams entries through
/// <see cref="ISkillPackageWriter"/> and <see cref="ISkillPackageReader"/>.
/// Pages through <see cref="IEmpiricalMemory.BrowseAsync(int, int, EmpiricalKind?, string?, CancellationToken)"/> on export,
/// applying quality gates per entry.
/// </summary>
public sealed class SkillPackager : ISkillPackager
{
    private const int PageSize = 100;

    /// <inheritdoc />
    public async Task<SkillExportResult> ExportAsync(
        SkillExportOptions options,
        IEmpiricalMemory memory,
        ISkillPackageWriter writer,
        IEpisodeStore? episodes = null,
        TagImportanceMap? tagImportances = null,
        CancellationToken ct = default)
    {
        await writer.WriteHeaderAsync(new SkillPackageHeader
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = options.Name,
            Domain = options.Domain,
            Version = options.Version,
            Description = options.Description,
            TagImportances = tagImportances
        }, ct);

        var entriesExported = 0;
        var episodeIds = new HashSet<string>();
        var offset = 0;

        while (true)
        {
            var page = await memory.BrowseAsync(offset, PageSize, options.Kind, ct: ct);
            if (page.Count == 0) break;

            foreach (var entry in page)
            {
                if (!PassesQualityGates(entry, options)) continue;

                await writer.WriteEntryAsync(entry, ct);
                entriesExported++;

                if (entry.EpisodeId is not null)
                    episodeIds.Add(entry.EpisodeId);
            }

            offset += page.Count;
        }

        var episodesExported = 0;
        if (options.IncludeEpisodes && episodes is not null)
        {
            foreach (var id in episodeIds)
            {
                var episode = await episodes.GetAsync(id, ct);
                if (episode is null) continue;

                await writer.WriteEpisodeAsync(episode, ct);
                episodesExported++;
            }
        }

        await writer.CompleteAsync(new TrainingManifest
        {
            TotalEntries = entriesExported,
            TotalEpisodes = episodesExported,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);

        return new SkillExportResult
        {
            EntriesExported = entriesExported,
            EpisodesExported = episodesExported
        };
    }

    /// <inheritdoc />
    public async Task<SkillImportResult> ImportAsync(
        ISkillPackageReader reader,
        IEmpiricalMemory memory,
        IEpisodeStore? episodes = null,
        SkillImportOptions? options = null,
        CancellationToken ct = default)
    {
        var opts = options ?? new SkillImportOptions();
        var added = 0;
        var merged = 0;
        var skipped = 0;

        await reader.ReadHeaderAsync(ct);

        await foreach (var entry in reader.ReadEntriesAsync(ct))
        {
            var importedEntry = entry with
            {
                Id = opts.IdPrefix is not null ? $"{opts.IdPrefix}{entry.Id}" : entry.Id,
                Strength = entry.Strength * opts.StrengthScale,
                Source = opts.EvidenceSource
            };

            if (opts.Mode == SkillImportMode.AddOnly)
            {
                var existing = await memory.GetAsync(importedEntry.Id, ct);
                if (existing is not null)
                {
                    skipped++;
                    continue;
                }
            }

            var committed = await memory.CommitAsync(importedEntry, ct);
            if (committed.Id != importedEntry.Id)
                merged++;
            else
                added++;
        }

        var episodesImported = 0;
        if (episodes is not null)
        {
            await foreach (var episode in reader.ReadEpisodesAsync(ct))
            {
                await episodes.CommitAsync(episode, ct);
                episodesImported++;
            }
        }

        return new SkillImportResult
        {
            EntriesAdded = added,
            EntriesMerged = merged,
            EntriesSkipped = skipped,
            EpisodesImported = episodesImported
        };
    }

    private static bool PassesQualityGates(EmpiricalEntry entry, SkillExportOptions options)
    {
        if (entry.ConsolidatedInto is not null) return false;
        if (entry.Strength < options.MinStrength) return false;
        if (entry.Confidence < options.MinConfidence) return false;
        if (entry.ObservationCount < options.MinObservations) return false;
        if (options.RequiredTags is not null
            && !options.RequiredTags.Any(t => entry.Tags.Contains(t)))
            return false;
        return true;
    }
}

using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Features;

/// <summary>
/// Default implementation of <see cref="ITagImportanceTracker"/>.
/// Pages through all entries in empirical memory, counts positive and negative
/// occurrences of each tag, and normalizes to [0.0, 1.0].
/// </summary>
/// <remarks>
/// <para>
/// For each tag <c>t</c>:
/// <code>
/// positive(t) = count of entries where tag t present AND Valence &gt; 0
/// negative(t) = count of entries where tag t present AND Valence &lt; 0
/// total(t)    = positive(t) + negative(t)
/// raw(t)      = (positive(t) - negative(t)) / total(t)   // in [-1, 1]
/// importance(t) = (raw(t) + 1) / 2                        // in [0, 1]
/// </code>
/// </para>
/// <para>
/// Tags appearing only in positive entries → importance ≈ 1.0.
/// Tags appearing equally in positive and negative → importance ≈ 0.5.
/// Tags appearing only in negative entries → importance ≈ 0.0.
/// </para>
/// </remarks>
public sealed class TagImportanceTracker(
    TagImportanceOptions? options = null) : ITagImportanceTracker
{
    private readonly TagImportanceOptions _options = options ?? new();

    /// <inheritdoc />
    public async Task<TagImportanceMap?> ComputeAsync(
        IEmpiricalMemory memory, CancellationToken ct = default)
    {
        var positiveCounts = new Dictionary<string, int>();
        var negativeCounts = new Dictionary<string, int>();
        var entriesWithValence = 0;

        // Page through all entries
        var offset = 0;
        const int pageSize = 100;
        while (true)
        {
            var page = await memory.BrowseAsync(offset, pageSize, ct: ct);
            if (page.Count == 0) break;

            foreach (var entry in page)
            {
                // Skip entries with neutral valence — they carry no outcome signal
                if (entry.Valence == 0f) continue;

                entriesWithValence++;
                var isPositive = entry.Valence > 0f;

                foreach (var tag in entry.Tags)
                {
                    var target = isPositive ? positiveCounts : negativeCounts;
                    target[tag] = target.GetValueOrDefault(tag) + 1;
                }
            }

            offset += page.Count;
        }

        // Guard: not enough data
        if (entriesWithValence < _options.MinSampleSize)
            return null;

        // Compute importance for each observed tag
        var allTags = positiveCounts.Keys.Union(negativeCounts.Keys);
        var importances = new Dictionary<string, float>();

        foreach (var tag in allTags)
        {
            var positive = positiveCounts.GetValueOrDefault(tag);
            var negative = negativeCounts.GetValueOrDefault(tag);
            var total = positive + negative;

            // raw in [-1, 1], normalized to [0, 1]
            var raw = (float)(positive - negative) / total;
            importances[tag] = (raw + 1f) / 2f;
        }

        return new TagImportanceMap
        {
            Importances = importances,
            EntriesAnalyzed = entriesWithValence,
            ComputedAt = DateTimeOffset.UtcNow
        };
    }
}

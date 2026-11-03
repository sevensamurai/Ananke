using System.Diagnostics.CodeAnalysis;

namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// How a store applies <see cref="SearchOptions.MaxPerSource"/>: rank <see cref="Candidates"/> results, then keep at
/// most that many per <c>source</c>, in rank order, up to <see cref="SearchOptions.TopK"/>.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public static class SearchResultLimits
{
    /// <summary>What joins a document's passages into one result under <see cref="SearchOptions.JoinPerSource"/>.</summary>
    public const string PassageSeparator = "\n\n[…]\n\n";

    /// <summary>How many more candidates a store ranks when results are limited per source.</summary>
    public const int CandidatesPerResult = 8;

    /// <summary>How many results a store ranks before <see cref="LimitPerSource"/>: <see cref="SearchOptions.TopK"/>, or more when results are limited per source.</summary>
    public static int Candidates(this SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.MaxPerSource is null ? options.TopK : options.TopK * CandidatesPerResult;
    }

    /// <summary>The ranked <paramref name="chunks"/>, at most <see cref="SearchOptions.MaxPerSource"/> per source, up to <see cref="SearchOptions.TopK"/>.</summary>
    public static IReadOnlyList<KnowledgeChunk> LimitPerSource(this IEnumerable<KnowledgeChunk> chunks, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxPerSource is not { } max)
            return [.. chunks.Take(options.TopK)];

        if (options.JoinPerSource)
            return Joined(chunks, max, options.TopK);

        var perSource = new Dictionary<string, int>(StringComparer.Ordinal);
        var kept = new List<KnowledgeChunk>(options.TopK);
        foreach (var chunk in chunks)
        {
            var source = chunk.Metadata.GetValueOrDefault("source", chunk.Id);
            var count = perSource.GetValueOrDefault(source);
            if (count >= max)
                continue;
            perSource[source] = count + 1;
            kept.Add(chunk);
            if (kept.Count == options.TopK)
                break;
        }
        return kept;
    }

    /// <summary>The first <paramref name="topK"/> sources in rank order, each one result: its best chunk, with up to <paramref name="max"/> of its passages joined.</summary>
    private static IReadOnlyList<KnowledgeChunk> Joined(IEnumerable<KnowledgeChunk> chunks, int max, int topK)
    {
        var bySource = new Dictionary<string, List<KnowledgeChunk>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var chunk in chunks)
        {
            var source = chunk.Metadata.GetValueOrDefault("source", chunk.Id);
            if (!bySource.TryGetValue(source, out var passages))
            {
                if (order.Count == topK)
                    continue;
                bySource[source] = passages = [];
                order.Add(source);
            }
            if (passages.Count < max)
                passages.Add(chunk);
        }

        return [.. order.Select(source => Join(bySource[source]))];
    }

    private static KnowledgeChunk Join(List<KnowledgeChunk> passages) =>
        passages.Count == 1 ? passages[0] : passages[0] with { Text = string.Join(PassageSeparator, passages.Select(p => p.Text)) };
}

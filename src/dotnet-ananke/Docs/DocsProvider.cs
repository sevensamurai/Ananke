namespace Ananke.Tool.Docs;

/// <summary>
/// Discovers and reads documentation Markdown files from the repository's <c>docs/</c> directory.
/// Locates the docs root by walking up from the current working directory to find
/// <c>Ananke.slnx</c> or <c>.ananke.yml</c>, then resolving <c>../docs</c> from there.
/// </summary>
/// <remarks>
/// Only indexes actionable documentation (<c>guides/</c>, <c>reference/</c>, and root-level
/// files like FAQ). Branding and editorial content (<c>about/</c>, <c>learning.md</c>) is
/// excluded — it helps GitHub browsers but not users debugging a fork/join error.
/// </remarks>
internal static class DocsProvider
{
    private static readonly string[] SentinelFiles = ["Ananke.slnx", ".ananke.yml"];

    /// <summary>Subdirectories to index. Files in the docs root are always included.</summary>
    private static readonly string[] IndexedCategories = ["guides", "reference"];

    /// <summary>
    /// Attempts to locate the <c>docs/</c> directory relative to the current working directory.
    /// Returns <c>null</c> if not found (e.g. running from a NuGet-installed tool outside the repo).
    /// </summary>
    public static string? FindDocsRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (dir is not null)
        {
            foreach (var sentinel in SentinelFiles)
            {
                if (File.Exists(Path.Combine(dir.FullName, sentinel)))
                {
                    // Sentinel found in dir — docs/ is a sibling (for .slnx inside src/)
                    // or a child (for repo root). Try both.
                    var docsDir = Path.Combine(dir.FullName, "..", "docs");
                    if (Directory.Exists(docsDir))
                        return Path.GetFullPath(docsDir);

                    docsDir = Path.Combine(dir.FullName, "docs");
                    if (Directory.Exists(docsDir))
                        return Path.GetFullPath(docsDir);
                }
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Discovers documentation topics from the indexed subdirectories and root of <c>docs/</c>.
    /// Returns an empty list if the docs root cannot be found.
    /// </summary>
    public static IReadOnlyList<DocsTopic> ListTopics()
    {
        var docsRoot = FindDocsRoot();
        if (docsRoot is null)
            return [];

        var topics = new List<DocsTopic>();

        // Root-level files (e.g. faq.md)
        foreach (var file in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            // Skip table-of-contents / index files that duplicate --list output
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Equals("learning", StringComparison.OrdinalIgnoreCase))
                continue;

            var topic = ParseTopic(file, docsRoot);
            if (topic is not null)
                topics.Add(topic);
        }

        // Indexed subdirectories
        foreach (var category in IndexedCategories)
        {
            var categoryDir = Path.Combine(docsRoot, category);
            if (!Directory.Exists(categoryDir))
                continue;

            foreach (var file in Directory.EnumerateFiles(categoryDir, "*.md", SearchOption.AllDirectories))
            {
                var topic = ParseTopic(file, docsRoot);
                if (topic is not null)
                    topics.Add(topic);
            }
        }

        topics.Sort((a, b) =>
        {
            var catCmp = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            return catCmp != 0 ? catCmp : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });

        return topics;
    }

    /// <summary>
    /// Finds a topic by key (case-insensitive, prefix-match).
    /// For example, <c>"getting-started"</c> matches <c>"01-getting-started"</c>.
    /// </summary>
    public static DocsTopic? FindTopic(string query)
    {
        var topics = ListTopics();
        var normalized = query.Trim().ToLowerInvariant();

        // Exact key match
        var match = topics.FirstOrDefault(t =>
            string.Equals(t.Key, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;

        // Suffix match: "getting-started" matches "01-getting-started"
        match = topics.FirstOrDefault(t =>
            t.Key.EndsWith(normalized, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;

        // Contains match
        match = topics.FirstOrDefault(t =>
            t.Key.Contains(normalized, StringComparison.OrdinalIgnoreCase));

        return match;
    }

    /// <summary>
    /// Reads the full Markdown content of a topic.
    /// </summary>
    public static string ReadContent(DocsTopic topic) => File.ReadAllText(topic.FullPath);

    /// <summary>
    /// Searches all topics for lines containing the query string.
    /// Returns matches with the topic, matching section heading, and a context snippet.
    /// </summary>
    public static IReadOnlyList<SearchResult> Search(string query)
    {
        var topics = ListTopics();
        var results = new List<SearchResult>();
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var topic in topics)
        {
            var content = ReadContent(topic);
            var lines = content.Split('\n');
            var currentSection = topic.Title;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.StartsWith("## "))
                    currentSection = line[3..].Trim();

                // Check if line contains all search terms
                if (terms.All(t => line.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    // Build a 3-line snippet around the match
                    var snippetStart = Math.Max(0, i - 1);
                    var snippetEnd = Math.Min(lines.Length - 1, i + 1);
                    var snippet = string.Join('\n', lines[snippetStart..(snippetEnd + 1)]).Trim();

                    results.Add(new SearchResult
                    {
                        Topic = topic,
                        Section = currentSection,
                        Snippet = snippet.Length > 300 ? snippet[..300] + "..." : snippet,
                    });

                    break; // One result per topic
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Extracts the <c>## </c> section headings from a Markdown file.
    /// </summary>
    public static IReadOnlyList<string> ExtractSections(string content) =>
        content.Split('\n')
            .Where(l => l.StartsWith("## "))
            .Select(l => l[3..].Trim())
            .ToList();

    private static DocsTopic? ParseTopic(string fullPath, string docsRoot)
    {
        var relativePath = Path.GetRelativePath(docsRoot, fullPath).Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(fullPath);

        // Category from directory: "guides/01-getting-started.md" → "guides"
        var dir = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
        var category = string.IsNullOrEmpty(dir) ? "general" : dir;

        // Title from first # heading
        var title = fileName;
        try
        {
            using var reader = new StreamReader(fullPath);
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("# ") && !line.StartsWith("##"))
                {
                    title = line[2..].Trim();
                    break;
                }
            }
        }
        catch
        {
            // If we can't read the file, skip it
            return null;
        }

        return new DocsTopic
        {
            Key = fileName.ToLowerInvariant(),
            Category = category,
            Title = title,
            RelativePath = relativePath,
            FullPath = Path.GetFullPath(fullPath),
        };
    }
}

/// <summary>
/// A search result from <see cref="DocsProvider.Search"/>.
/// </summary>
internal sealed record SearchResult
{
    /// <summary>The topic containing the match.</summary>
    public required DocsTopic Topic { get; init; }

    /// <summary>The <c>## </c> section heading nearest above the matching line.</summary>
    public required string Section { get; init; }

    /// <summary>A context snippet around the matching line (up to 300 chars).</summary>
    public required string Snippet { get; init; }
}

using Ananke.Tool.Docs;
using Ananke.Tool.Output;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke docs</c> — list, read, and search Ananke documentation
/// from the terminal. Supports <c>--json</c> for agent-optimized output.
/// </summary>
/// <remarks>
/// Documentation is read from the repository's <c>docs/</c> directory,
/// located by walking up from CWD to find <c>Ananke.slnx</c>.
/// </remarks>
internal static class DocsCommand
{
    public static Command Create()
    {
        var topicArg = new Argument<string?>("topic")
        {
            Description = "Topic to display (e.g. 'getting-started', 'dsl-syntax', 'workflows').",
            DefaultValueFactory = _ => null,
        };

        var listOption = new Option<bool>("--list")
        {
            Description = "List all available documentation topics."
        };

        var searchOption = new Option<string?>("--search")
        {
            Description = "Search across all documentation for the given terms."
        };

        var command = new Command("docs", "Browse, read, and search Ananke framework documentation.")
        {
            topicArg,
            listOption,
            searchOption
        };

        command.SetAction(parseResult =>
        {
            var topic = parseResult.GetValue(topicArg);
            var list = parseResult.GetValue(listOption);
            var search = parseResult.GetValue(searchOption);
            var json = parseResult.GetValue<bool>("--json");

            if (search is not null)
                ExecuteSearch(search, json);
            else if (list || topic is null)
                ExecuteList(json);
            else
                ExecuteRead(topic, json);
        });

        return command;
    }

    private static void ExecuteList(bool json)
    {
        var topics = DocsProvider.ListTopics();

        if (topics.Count == 0)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = "Documentation directory not found. Run from inside the Ananke repository." });
            else
                Console.Error.WriteLine("  Documentation directory not found. Run from inside the Ananke repository.");
            return;
        }

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                count = topics.Count,
                topics = topics.Select(t => new
                {
                    key = t.Key,
                    category = t.Category,
                    title = t.Title,
                    path = t.RelativePath,
                }).ToList()
            });
            return;
        }

        // Human-formatted grouped list
        Console.WriteLine("  Available Documentation");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine();

        var grouped = topics.GroupBy(t => t.Category).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            Console.WriteLine($"  {group.Key}/");
            foreach (var topic in group)
            {
                Console.WriteLine($"    {topic.Key,-40} {topic.Title}");
            }
            Console.WriteLine();
        }

        Console.WriteLine("  Usage: nnke docs <topic>");
        Console.WriteLine("  Search: nnke docs --search \"<query>\"");
    }

    private static void ExecuteRead(string query, bool json)
    {
        var topic = DocsProvider.FindTopic(query);

        if (topic is null)
        {
            if (json)
                JsonOutput.Write(new { status = "not_found", query, hint = "Run 'nnke docs --list' to see available topics." });
            else
                Console.Error.WriteLine($"  Topic not found: {query}");
            return;
        }

        var content = DocsProvider.ReadContent(topic);
        var sections = DocsProvider.ExtractSections(content);

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                topic = topic.Key,
                title = topic.Title,
                category = topic.Category,
                source = topic.RelativePath,
                sections,
                content,
            });
        }
        else
        {
            Console.WriteLine(content);
        }
    }

    private static void ExecuteSearch(string query, bool json)
    {
        var results = DocsProvider.Search(query);

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                query,
                count = results.Count,
                results = results.Select(r => new
                {
                    topic = r.Topic.Key,
                    category = r.Topic.Category,
                    section = r.Section,
                    snippet = r.Snippet,
                }).ToList()
            });
            return;
        }

        if (results.Count == 0)
        {
            Console.WriteLine($"  No results for: {query}");
            return;
        }

        Console.WriteLine($"  Search results for: {query}");
        Console.WriteLine($"  ─────────────────────────────────────────────────");
        Console.WriteLine();

        foreach (var result in results)
        {
            Console.WriteLine($"  [{result.Topic.Category}/{result.Topic.Key}]  § {result.Section}");
            Console.WriteLine($"    {result.Snippet.Replace("\n", "\n    ")}");
            Console.WriteLine();
        }

        Console.WriteLine($"  {results.Count} result(s). Read a topic: nnke docs <topic>");
    }
}

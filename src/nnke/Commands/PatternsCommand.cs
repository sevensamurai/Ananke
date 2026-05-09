using Ananke.Tool.Shared;
using Ananke.Tool.Patterns;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke patterns</c> — lists all recognized workflow and agentic patterns,
/// or shows detailed information for a specific pattern.
/// Supports <c>--json</c> for agent consumption.
/// </summary>
internal static class PatternsCommand
{
    public static Command Create()
    {
        var patternArg = new Argument<string?>("pattern")
        {
            Description = "Pattern to describe (e.g. 'review-critique', 'etl'). Omit to list all.",
            DefaultValueFactory = _ => null,
        };

        var command = new Command("patterns", "List and describe recognized workflow and agentic patterns.")
        {
            patternArg
        };

        command.SetAction(parseResult =>
        {
            var pattern = parseResult.GetValue(patternArg);
            var json = parseResult.GetValue<bool>("--json");

            if (pattern is null)
                ExecuteList(json);
            else
                ExecuteDescribe(pattern, json);
        });

        return command;
    }

    private static void ExecuteList(bool json)
    {
        var all = PatternCatalog.All();

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                count = all.Count,
                manifestPatterns = PatternCatalog.ManifestPatterns().Select(p => new
                {
                    key = p.Key,
                    title = p.Title,
                    topology = p.Topology,
                }).ToList(),
                agenticPatterns = PatternCatalog.AgenticPatterns().Select(p => new
                {
                    key = p.Key,
                    title = p.Title,
                    topology = p.Topology,
                }).ToList(),
            });
            return;
        }

        Console.WriteLine("  Ananke Pattern Catalog");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine();

        Console.WriteLine("  Topology patterns (manifest-driven):");
        foreach (var p in PatternCatalog.ManifestPatterns())
            Console.WriteLine($"    {p.Key,-25} {p.Topology}");

        Console.WriteLine();
        Console.WriteLine("  Agentic patterns (code-driven):");
        foreach (var p in PatternCatalog.AgenticPatterns())
            Console.WriteLine($"    {p.Key,-25} {p.Topology}");

        Console.WriteLine();
        Console.WriteLine("  Use: nnke patterns <pattern>");
        Console.WriteLine("  Scaffold: nnke new workflow <name> --pattern <pattern>");
    }

    private static void ExecuteDescribe(string key, bool json)
    {
        var pattern = PatternCatalog.Find(key);

        if (pattern is null)
        {
            if (json)
            {
                JsonOutput.Write(new
                {
                    status = "not_found",
                    key,
                    hint = "Run 'nnke patterns' to list all known patterns."
                });
            }
            else
            {
                Console.Error.WriteLine($"  Unknown pattern: {key}");
                Console.Error.WriteLine("  Run 'nnke patterns' to list all known patterns.");
            }
            return;
        }

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                key = pattern.Key,
                title = pattern.Title,
                style = pattern.Style,
                topology = pattern.Topology,
                dslEquivalent = pattern.DslEquivalent,
                apiEntryPoint = pattern.ApiEntryPoint,
                useCases = pattern.UseCases,
                scaffoldCommand = pattern.ScaffoldCommand,
                docsRef = pattern.DocsRef,
                description = pattern.Description.Trim(),
                apiExample = pattern.ApiExample.Trim(),
            });
            return;
        }

        Console.WriteLine($"  {pattern.Key} — {pattern.Title}");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine($"  Style:    {pattern.Style}");
        Console.WriteLine($"  Topology: {pattern.Topology}");
        if (pattern.DslEquivalent is not null)
        {
            Console.WriteLine("  DSL:");
            foreach (var line in pattern.DslEquivalent.Split('\n'))
                Console.WriteLine($"    {line}");
        }

        Console.WriteLine();
        WriteIndented(pattern.Description);

        Console.WriteLine();
        Console.WriteLine("  Use cases:");
        foreach (var uc in pattern.UseCases)
            Console.WriteLine($"    • {uc}");

        Console.WriteLine();
        Console.WriteLine("  Scaffold:");
        Console.WriteLine($"    {pattern.ScaffoldCommand}");

        Console.WriteLine();
        Console.WriteLine("  API:");
        Console.WriteLine($"    {pattern.ApiEntryPoint}");
        Console.WriteLine();
        foreach (var line in pattern.ApiExample.Trim().Split('\n'))
            Console.WriteLine($"    {line.TrimEnd()}");

        Console.WriteLine();
        Console.WriteLine($"  Guide: {pattern.DocsRef}");
    }

    private static void WriteIndented(string text)
    {
        foreach (var line in text.Trim().Split('\n'))
            Console.WriteLine($"    {line.TrimEnd()}");
    }
}

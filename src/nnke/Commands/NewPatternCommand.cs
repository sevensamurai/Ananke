using Ananke.Tool.Patterns;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke new pattern &lt;name&gt; --pattern &lt;key&gt;</c>.
/// A forward-looking alias that delegates to <see cref="NewWorkflowCommand"/>
/// for named agentic-design-pattern scaffolds. Exposes the pattern catalog
/// directly so users discover patterns before reaching for <c>nnke patterns</c>.
/// </summary>
/// <remarks>
/// This command is intentionally thin: it adds discoverability ("I want a pattern")
/// without duplicating scaffold logic. As pattern-specific templates are added,
/// they will automatically be available here through the shared
/// <see cref="PatternCatalog"/> and <see cref="NewWorkflowCommand"/> dispatch.
/// </remarks>
internal static class NewPatternCommand
{
    public static Command Create()
    {
        var nameArg = new Argument<string>("name") { Description = "Name for the generated project." };

        var patternOption = new Option<string>("--pattern")
        {
            Description = "Agentic design pattern to scaffold. Run 'nnke patterns' for the full catalog.",
            DefaultValueFactory = _ => "router"
        };

        var providerOption = new Option<string>("--provider")
        {
            Description = "LLM provider to configure (openai, anthropic, google).",
            DefaultValueFactory = _ => "openai"
        };

        var outputOption = new Option<DirectoryInfo?>("--output")
        {
            Description = "Output directory. Defaults to ./<name>."
        };

        var listOption = new Option<bool>("--list")
        {
            Description = "List all available agentic patterns and exit."
        };

        var command = new Command("pattern",
            "Scaffold an Ananke project from an agentic design pattern. Use --list to see all patterns.")
        {
            nameArg,
            patternOption,
            providerOption,
            outputOption,
            listOption
        };

        // name is not required when --list is used
        nameArg.Arity = ArgumentArity.ZeroOrOne;

        command.SetAction(parseResult =>
        {
            var list = parseResult.GetValue(listOption);
            var json = parseResult.GetValue<bool>("--json");

            if (list)
            {
                ListPatterns(json);
                return;
            }

            var name = parseResult.GetValue(nameArg);
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("  Provide a project name or use --list to see available patterns.");
                return;
            }

            var pattern = parseResult.GetValue(patternOption)!;
            var provider = parseResult.GetValue(providerOption)!;
            var output = parseResult.GetValue(outputOption);

            // Delegate to NewWorkflowCommand which owns the scaffold logic
            NewWorkflowCommand.ExecuteForCli(name, provider, pattern, output, json);
        });

        return command;
    }

    private static void ListPatterns(bool json)
    {
        var all = PatternCatalog.All();
        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                count = all.Count,
                patterns = all.Select(p => new
                {
                    key = p.Key,
                    title = p.Title,
                    topology = p.Topology,
                    scaffold = $"nnke new pattern <name> --pattern {p.Key}",
                })
            });
            return;
        }

        Console.WriteLine("  Agentic Design Patterns");
        Console.WriteLine();
        foreach (var p in PatternCatalog.AgenticPatterns())
            Console.WriteLine($"    {p.Key,-28} {p.Title}");
        Console.WriteLine();
        Console.WriteLine("  Manifest-Driven Topology Patterns");
        Console.WriteLine();
        foreach (var p in PatternCatalog.ManifestPatterns())
            Console.WriteLine($"    {p.Key,-28} {p.Title}");
        Console.WriteLine();
        Console.WriteLine("  Example:");
        Console.WriteLine("    nnke new pattern my-agent --pattern router");
        Console.WriteLine("    nnke new pattern my-agent --pattern review-critique");
    }
}

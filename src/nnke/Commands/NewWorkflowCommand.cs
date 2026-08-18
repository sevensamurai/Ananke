using Ananke.Tool.Shared;
using Ananke.Tool.Patterns;
using Ananke.Tool.Templates;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke new workflow &lt;name&gt;</c> — scaffolds a complete
/// runnable workflow project with <c>.csproj</c>, <c>Program.cs</c>,
/// <c>.ananke.yml</c>, state record, and secrets template.
/// Supports <c>--json</c> for structured output.
/// </summary>
internal static class NewWorkflowCommand
{
    public static Command Create()
    {
        var nameArg = new Argument<string>("name") { Description = "Name for the workflow project." };

        var providerOption = new Option<string>("--provider")
        {
            Description = "LLM provider to configure (openai, anthropic, google).",
            DefaultValueFactory = _ => "openai"
        };

        var patternOption = new Option<string>("--pattern")
        {
            Description = "Workflow pattern. Run 'nnke patterns' for the full catalog.",
            DefaultValueFactory = _ => "etl"
        };

        var outputOption = new Option<DirectoryInfo?>("--output")
        {
            Description = "Output directory. Defaults to ./<name>."
        };

        var command = new Command("workflow", "Scaffold a complete Ananke workflow project.")
        {
            nameArg,
            providerOption,
            patternOption,
            outputOption
        };

        command.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArg)!;
            var provider = parseResult.GetValue(providerOption)!;
            var pattern = parseResult.GetValue(patternOption)!;
            var output = parseResult.GetValue(outputOption);
            var json = parseResult.GetValue<bool>("--json");
            return Execute(name, provider, pattern, output, json);
        });

        return command;
    }

    /// <summary>Manifest-driven patterns that generate a <c>.ananke.yml</c> file.</summary>
    private static readonly HashSet<string> ManifestPatterns = ["etl", "fan-out", "sequential", "sub-workflow"];

    /// <summary>Code-only patterns that use neither a manifest nor Ananke.Design.</summary>
    private static readonly HashSet<string> CodeOnlyPatterns =
        ["review-critique", "iterative-refinement", "router", "human-in-the-loop", "handoff", "organic-host", "streaming-chat"];

    private static int Execute(string name, string provider, string pattern, DirectoryInfo? output, bool json,
        List<string>? filesOverride = null, List<string>? skippedOverride = null)
    {
        if (!ProjectNameValidator.IsValid(name))
        {
            if (filesOverride is not null)
                throw new ArgumentException($"Invalid project name: '{name}'");

            if (json)
                JsonOutput.Write(new { status = "error", errors = new[] { new { code = "ANANKE_IO_002", message = $"Invalid project name: '{name}'" } } });
            else
            {
                Console.Error.WriteLine($"  ✗ [ANANKE_IO_002] Invalid project name: '{name}'");
                Console.Error.WriteLine("    Hint: Use only letters, numbers, hyphens, underscores, and periods.");
            }
            return 1;
        }

        // Validate the pattern exists in the catalog
        var catalogEntry = PatternCatalog.Find(pattern);
        if (catalogEntry is null)
        {
            var known = string.Join(", ", PatternCatalog.All().Select(p => p.Key));
            if (json)
            {
                JsonOutput.Write(new { status = "error", message = $"Unknown pattern: {pattern}", knownPatterns = known });
            }
            else
            {
                Console.Error.WriteLine($"  Unknown pattern: {pattern}");
                Console.Error.WriteLine($"  Known patterns: {known}");
                Console.Error.WriteLine("  Run 'nnke patterns' for details.");
            }
            return 1;
        }

        var projectDir = output?.FullName ?? Path.Combine(Directory.GetCurrentDirectory(), name);
        Directory.CreateDirectory(projectDir);

        var files = filesOverride ?? new List<string>();
        var skipped = skippedOverride ?? new List<string>();
        var isManifest = ManifestPatterns.Contains(pattern);

        // .csproj — code patterns skip Ananke.Design
        WriteFile(projectDir, $"{name}.csproj", ProjectTemplate.Render(name, provider, pattern), files, skipped);

        // Program.cs — pattern-specific template
        WriteFile(projectDir, "Program.cs", ProgramTemplate.Render(name, pattern, provider), files, skipped);

        // Manifest and README — only for manifest-driven patterns
        if (isManifest)
        {
            WriteFile(projectDir, $"{name}.ananke.yml", ManifestTemplate.Render(name, provider, pattern), files, skipped);
            WriteFile(projectDir, "README.md", ReadmeTemplate.Render(name, provider), files, skipped);
        }

        // State record — pattern-specific type
        var (stateFileName, stateContent) = StateTemplate.RenderForPattern(name, pattern);
        WriteFile(projectDir, stateFileName, stateContent, files, skipped);

        // Secrets
        WriteFile(projectDir, "secrets.json", SecretsTemplate.Render(provider), files, skipped);

        if (filesOverride is not null) return 0; // MCP path — caller reads the lists directly

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "created",
                projectDir,
                pattern,
                provider,
                files,
                skipped = skipped.Count > 0 ? skipped : null as object,
            });
        }
        else
        {
            Console.WriteLine($"  Created workflow project: {projectDir}");
            Console.WriteLine();
            Console.WriteLine("  Files:");
            foreach (var f in files)
                Console.WriteLine($"    {f}");
            foreach (var s in skipped)
                Console.WriteLine($"    {s}  (skipped — exists)");
            Console.WriteLine();
            Console.WriteLine("  Next steps:");
            Console.WriteLine($"    cd {name}");
            Console.WriteLine($"    # Add your API key to secrets.json");
            Console.WriteLine($"    dotnet run");
        }

        return 0;
    }

    private static void WriteFile(string dir, string fileName, string content, List<string> created, List<string> skipped)
    {
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
        {
            skipped.Add(fileName);
            return;
        }

        File.WriteAllText(path, content);
        created.Add(fileName);
    }

    /// <summary>
    /// Entry point for the MCP <c>ananke_scaffold</c> tool. Same logic as
    /// <see cref="Execute"/> but writes into <paramref name="files"/> / <paramref name="skipped"/>
    /// lists instead of printing to the console.
    /// </summary>
    internal static void ExecuteForMcp(
        string name, string provider, string pattern,
        DirectoryInfo output, List<string> files, List<string> skipped)
    {
        Execute(name, provider, pattern, output, json: false, filesOverride: files, skippedOverride: skipped);
    }

    /// <summary>
    /// Entry point for commands that delegate pattern scaffold to this command
    /// (e.g. <c>nnke new pattern</c>). Passes through to <see cref="Execute"/>.
    /// </summary>
    internal static int ExecuteForCli(
        string name, string provider, string pattern, DirectoryInfo? output, bool json) =>
        Execute(name, provider, pattern, output, json);
}

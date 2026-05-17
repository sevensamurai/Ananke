using Ananke.Tool.Shared;
using Ananke.Tool.Templates;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke new quickstart &lt;name&gt;</c> — scaffolds a minimal Ananke
/// console project aligned with Guide 01 (Getting Started).
/// <para>
/// Uses <c>SimulatedModel</c> by default so the project runs without an API key.
/// Pass <c>--provider</c> to pre-configure provider comments and package references.
/// </para>
/// </summary>
internal static class NewQuickstartCommand
{
    public static Command Create()
    {
        var nameArg = new Argument<string>("name") { Description = "Name for the quickstart project." };

        var providerOption = new Option<string>("--provider")
        {
            Description = "LLM provider to pre-configure (openai, anthropic, google). Project still runs without an API key.",
            DefaultValueFactory = _ => "openai"
        };

        var outputOption = new Option<DirectoryInfo?>("--output")
        {
            Description = "Output directory. Defaults to ./<name>."
        };

        var command = new Command("quickstart",
            "Scaffold a beginner Ananke project that runs immediately — no API key required. Aligned with Guide 01.")
        {
            nameArg,
            providerOption,
            outputOption
        };

        command.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArg)!;
            var provider = parseResult.GetValue(providerOption)!;
            var output = parseResult.GetValue(outputOption);
            var json = parseResult.GetValue<bool>("--json");
            Execute(name, provider, output, json);
        });

        return command;
    }

    private static void Execute(string name, string provider, DirectoryInfo? output, bool json,
        List<string>? filesOverride = null, List<string>? skippedOverride = null)
    {
        var projectDir = output?.FullName ?? Path.Combine(Directory.GetCurrentDirectory(), name);
        Directory.CreateDirectory(projectDir);

        var files = filesOverride ?? [];
        var skipped = skippedOverride ?? [];

        WriteFile(projectDir, $"{name}.csproj",
            ProjectTemplate.Render(name, provider, "quickstart"), files, skipped);
        WriteFile(projectDir, "Program.cs",
            QuickstartTemplate.RenderProgram(name, provider), files, skipped);
        WriteFile(projectDir, "QuickstartState.cs",
            QuickstartTemplate.RenderState(name, provider), files, skipped);
        WriteFile(projectDir, "README.md",
            QuickstartTemplate.RenderReadme(name, provider), files, skipped);

        if (filesOverride is not null) return;

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "created",
                projectDir,
                pattern = "quickstart",
                provider,
                files,
                skipped = skipped.Count > 0 ? skipped : null as object,
            });
        }
        else
        {
            Console.WriteLine($"  Created quickstart project: {projectDir}");
            Console.WriteLine();
            Console.WriteLine("  Files:");
            foreach (var f in files)
                Console.WriteLine($"    {f}");
            foreach (var s in skipped)
                Console.WriteLine($"    {s}  (skipped — exists)");
            Console.WriteLine();
            Console.WriteLine("  Next steps:");
            Console.WriteLine($"    cd {name}");
            Console.WriteLine($"    dotnet run");
            Console.WriteLine();
            Console.WriteLine("  Read the full guide:");
            Console.WriteLine($"    nnke docs 01-getting-started");
        }
    }

    private static void WriteFile(string dir, string fileName, string content,
        List<string> created, List<string> skipped)
    {
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path)) { skipped.Add(fileName); return; }
        File.WriteAllText(path, content);
        created.Add(fileName);
    }

    /// <summary>Entry point for the MCP <c>ananke_scaffold</c> tool.</summary>
    internal static void ExecuteForMcp(
        string name, string provider, DirectoryInfo output,
        List<string> files, List<string> skipped) =>
        Execute(name, provider, output, json: false, filesOverride: files, skippedOverride: skipped);
}

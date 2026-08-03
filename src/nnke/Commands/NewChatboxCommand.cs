using Ananke.Tool.Shared;
using Ananke.Tool.Templates;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke new chatbox &lt;name&gt;</c> — scaffolds a streaming
/// conversational agent as an ASP.NET Minimal API with Server-Sent Events.
/// <para>
/// The generated project exposes <c>POST /chat</c> (full JSON) and
/// <c>POST /chat/stream</c> (SSE streaming) backed by
/// <see cref="Ananke.Orchestration.Agents.StreamingChatWorkflow"/>.
/// Uses <c>SimulatedStreamingModel</c> by default so it runs without an API key.
/// </para>
/// </summary>
internal static class NewChatboxCommand
{
    public static Command Create()
    {
        var nameArg = new Argument<string>("name") { Description = "Name for the chatbox project." };

        var providerOption = new Option<string>("--provider")
        {
            Description = "LLM provider to pre-configure (openai, anthropic, google). Project still runs without an API key.",
            DefaultValueFactory = _ => "openai"
        };

        var outputOption = new Option<DirectoryInfo?>("--output")
        {
            Description = "Output directory. Defaults to ./<name>."
        };

        var command = new Command("chatbox",
            "Scaffold a streaming conversational agent (Minimal API + SSE). Runs immediately — no API key required.")
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
            return Execute(name, provider, output, json);
        });

        return command;
    }

    private static int Execute(string name, string provider, DirectoryInfo? output, bool json,
        List<string>? filesOverride = null, List<string>? skippedOverride = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
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

        var projectDir = output?.FullName ?? Path.Combine(Directory.GetCurrentDirectory(), name);
        Directory.CreateDirectory(projectDir);

        var files = filesOverride ?? [];
        var skipped = skippedOverride ?? [];

        WriteFile(projectDir, $"{name}.csproj",
            ProjectTemplate.Render(name, provider, "chatbox"), files, skipped);
        WriteFile(projectDir, "Program.cs",
            ChatboxTemplate.RenderProgram(name, provider), files, skipped);
        WriteFile(projectDir, "ChatboxState.cs",
            ChatboxTemplate.RenderState(name, provider), files, skipped);
        WriteFile(projectDir, "secrets.json",
            SecretsTemplate.Render(provider), files, skipped);
        WriteFile(projectDir, "README.md",
            ChatboxTemplate.RenderReadme(name, provider), files, skipped);

        if (filesOverride is not null) return 0;

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "created",
                projectDir,
                pattern = "chatbox",
                provider,
                files,
                skipped = skipped.Count > 0 ? skipped : null as object,
            });
        }
        else
        {
            Console.WriteLine($"  Created chatbox project: {projectDir}");
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
            Console.WriteLine("  Test streaming:");
            Console.WriteLine("    curl -N -X POST http://localhost:5000/chat/stream \\");
            Console.WriteLine("         -H \"Content-Type: application/json\" \\");
            Console.WriteLine("         -d '{\"message\":\"Hello!\"}'");
            Console.WriteLine();
            Console.WriteLine("  Read the full guide:");
            Console.WriteLine($"    nnke docs 05-streaming");
        }

        return 0;
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

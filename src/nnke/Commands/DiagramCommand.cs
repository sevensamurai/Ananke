using Ananke.Design;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke diagram &lt;file&gt;</c> — loads an <c>.ananke.yml</c>
/// manifest and exports its topology as a Mermaid flowchart.
/// Supports <c>--json</c> for structured output.
/// </summary>
internal static class DiagramCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "Path to the .ananke.yml manifest file." };

        var outputOption = new Option<FileInfo?>("--output")
        {
            Description = "Output file for the Mermaid diagram. Defaults to stdout."
        };

        var command = new Command("diagram", "Export a Mermaid flowchart from an .ananke.yml manifest.")
        {
            fileArg,
            outputOption
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var output = parseResult.GetValue(outputOption);
            var json = parseResult.GetValue<bool>("--json");
            Execute(file, output, json);
        });

        return command;
    }

    private static void Execute(FileInfo file, FileInfo? output, bool json)
    {
        if (!file.Exists)
        {
            if (json)
            {
                JsonOutput.Write(new { status = "error", errors = new[] { new { code = "ANANKE_IO_001", message = $"File not found: {file.FullName}" } } });
            }
            else
            {
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            }
            return;
        }

        try
        {
            var manifest = WorkflowManifest.Load(file.FullName);
            var mermaid = RenderMermaid(manifest);

            if (json)
            {
                JsonOutput.Write(new
                {
                    status = "ok",
                    workflow = manifest.Name,
                    format = "mermaid",
                    diagram = mermaid,
                });
            }
            else if (output is not null)
            {
                File.WriteAllText(output.FullName, mermaid);
                Console.WriteLine($"  Diagram written to: {output.FullName}");
            }
            else
            {
                Console.WriteLine(mermaid);
            }
        }
        catch (Exception ex)
        {
            if (json)
            {
                JsonOutput.Write(new { status = "error", errors = new[] { new { code = "ANANKE_MANIFEST_001", message = ex.Message } } });
            }
            else
            {
                Console.Error.WriteLine($"  ✗ Diagram generation failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Renders a lightweight Mermaid flowchart directly from manifest connections,
    /// without requiring job bindings or a full workflow build.
    /// </summary>
    private static string RenderMermaid(WorkflowManifest manifest)
    {
        var lines = new List<string> { "graph TD" };

        // Emit nodes with shape hints based on job type.
        foreach (var (name, job) in manifest.Jobs)
        {
            var label = job.Type == "agent" ? $"🤖 {name}" : name;
            var shape = job.Type == "agent" ? $"    {NodeId(name)}{{\"{label}\"}}" : $"    {NodeId(name)}[\"{label}\"]";
            lines.Add(shape);
        }

        lines.Add("    _end([\"End\"])");
        lines.Add("");

        // Parse connection lines into edges.
        foreach (var connection in manifest.Connections)
        {
            var trimmed = connection.Trim();

            // fork: "a -> fork(b, c)"
            if (trimmed.Contains("fork("))
            {
                var parts = trimmed.Split("->", 2, StringSplitOptions.TrimEntries);
                var from = parts[0];
                var forkBody = parts[1].Replace("fork(", "").Replace(")", "");
                foreach (var target in forkBody.Split(',', StringSplitOptions.TrimEntries))
                    lines.Add($"    {NodeId(from)} -->|fork| {TargetId(target)}");
            }
            // join: "join(a, b) -> c"
            else if (trimmed.Contains("join("))
            {
                var parts = trimmed.Split("->", 2, StringSplitOptions.TrimEntries);
                var joinBody = parts[0].Replace("join(", "").Replace(")", "");
                var target = parts[1];
                foreach (var source in joinBody.Split(',', StringSplitOptions.TrimEntries))
                    lines.Add($"    {NodeId(source)} -->|join| {TargetId(target)}");
            }
            // direct: "a -> b"
            else if (trimmed.Contains("->"))
            {
                var parts = trimmed.Split("->", 2, StringSplitOptions.TrimEntries);
                lines.Add($"    {NodeId(parts[0])} --> {TargetId(parts[1])}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string NodeId(string name) =>
        name.Replace("-", "_").Replace(" ", "_");

    private static string TargetId(string name) =>
        string.Equals(name, "End", StringComparison.OrdinalIgnoreCase) ? "_end" : NodeId(name);
}

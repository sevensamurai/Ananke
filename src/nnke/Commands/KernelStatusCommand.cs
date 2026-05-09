using Ananke.Organics.Kernel.Snapshots;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke kernel status &lt;file&gt;</c> — displays alive cells,
/// their domains, tools, models, and routing table from a mesh snapshot.
/// </summary>
internal static class KernelStatusCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to a kernel snapshot YAML file."
        };

        var command = new Command("status", "Show active cells, domains, tools, and routing from a kernel snapshot.")
        {
            fileArg
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var json = parseResult.GetValue<bool>("--json");
            Execute(file, json);
        });

        return command;
    }

    private static void Execute(FileInfo file, bool json)
    {
        if (!file.Exists)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"File not found: {file.FullName}" });
            else
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            return;
        }

        HostSnapshot snapshot;
        try
        {
            var yaml = File.ReadAllText(file.FullName);
            snapshot = HostSnapshotExporter.FromYaml(yaml);
        }
        catch (Exception ex)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Failed to parse snapshot: {ex.Message}" });
            else
                Console.Error.WriteLine($"  Failed to parse snapshot: {ex.Message}");
            return;
        }

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                kernel = snapshot.KernelId,
                version = snapshot.Version,
                takenAt = snapshot.TakenAt,
                cellCount = snapshot.Cells.Count,
                cells = snapshot.Cells.Select(c => new
                {
                    name = c.Name,
                    domain = c.Domain,
                    splitFrom = c.SplitFrom,
                    toolCount = c.Tools.Count,
                    tools = c.Tools,
                    jobCount = c.Jobs.Count,
                    jobs = c.Jobs.Keys.ToList(),
                    modelCount = c.Models.Count,
                    models = c.Models.Keys.ToList(),
                    memoryDomains = c.MemoryProfile?.Domains,
                    lineageTags = c.MemoryProfile?.LineageTags
                }),
                routing = snapshot.RoutingTable
            });
            return;
        }

        // Human-readable output
        Console.WriteLine();
        Console.WriteLine($"  Kernel: {snapshot.KernelId}  (v{snapshot.Version}, {snapshot.TakenAt:yyyy-MM-dd HH:mm} UTC)");
        Console.WriteLine($"  Active cells: {snapshot.Cells.Count}");
        Console.WriteLine();

        foreach (var cell in snapshot.Cells)
        {
            var lineage = cell.SplitFrom is not null ? $"  (from {cell.SplitFrom})" : "";
            Console.WriteLine($"  ● {cell.Name}  [{cell.Domain}]{lineage}");

            if (cell.Tools.Count > 0)
                Console.WriteLine($"    Tools: {string.Join(", ", cell.Tools)}");

            if (cell.Jobs.Count > 0)
            {
                var agentJobs = cell.Jobs.Where(j => j.Value.Type.Equals("agent", StringComparison.OrdinalIgnoreCase)).Select(j => j.Key);
                var codeJobs = cell.Jobs.Where(j => j.Value.Type.Equals("code", StringComparison.OrdinalIgnoreCase)).Select(j => j.Key);
                var agents = agentJobs.ToList();
                var codes = codeJobs.ToList();
                if (agents.Count > 0)
                    Console.WriteLine($"    Agent jobs: {string.Join(", ", agents)}");
                if (codes.Count > 0)
                    Console.WriteLine($"    Code jobs: {string.Join(", ", codes)}");
            }

            if (cell.Models.Count > 0)
                Console.WriteLine($"    Models: {string.Join(", ", cell.Models.Select(m => $"{m.Key} ({m.Value.Provider}/{m.Value.Model})"))}");

            if (cell.MemoryProfile is not null)
                Console.WriteLine($"    Memory: domains=[{string.Join(", ", cell.MemoryProfile.Domains)}]" +
                    (cell.MemoryProfile.LineageTags.Count > 0 ? $"  lineage=[{string.Join(", ", cell.MemoryProfile.LineageTags)}]" : ""));

            Console.WriteLine();
        }

        if (snapshot.RoutingTable.Count > 0)
        {
            Console.WriteLine("  Routing:");
            foreach (var (domain, cellName) in snapshot.RoutingTable)
                Console.WriteLine($"    {domain} → {cellName}");
            Console.WriteLine();
        }
    }
}

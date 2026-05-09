using Ananke.Organics.Kernel.Snapshots;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke colony cell trace &lt;name&gt; &lt;file&gt;</c> — shows the
/// signal timeline for a specific cell: tools, domain, model, and division history.
/// </summary>
internal static class CellTraceCommand
{
    public static Command Create()
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "Cell name to trace."
        };

        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to a host snapshot YAML file."
        };

        var command = new Command("trace",
            "Show signal history and manifest details for a specific colony cell.")
        {
            nameArg,
            fileArg
        };

        command.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArg)!;
            var file = parseResult.GetValue(fileArg)!;
            var json = parseResult.GetValue<bool>("--json");
            Execute(name, file, json);
        });

        return command;
    }

    private static void Execute(string name, FileInfo file, bool json)
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
            snapshot = HostSnapshotExporter.FromYaml(File.ReadAllText(file.FullName));
        }
        catch (Exception ex)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Failed to parse snapshot: {ex.Message}" });
            else
                Console.Error.WriteLine($"  Failed to parse snapshot: {ex.Message}");
            return;
        }

        var cell = snapshot.Cells.FirstOrDefault(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (cell is null)
        {
            var available = string.Join(", ", snapshot.Cells.Select(c => c.Name));
            if (json)
                JsonOutput.Write(new { status = "not_found", name, available });
            else
                Console.Error.WriteLine($"  Cell '{name}' not found. Available: {available}");
            return;
        }

        // Division events relevant to this cell (as parent or child)
        var divEvents = snapshot.DivisionHistory
            .Where(d => d.ParentWorkflow.Equals(name, StringComparison.OrdinalIgnoreCase)
                     || d.Children.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                name = cell.Name,
                domain = cell.Domain,
                splitFrom = cell.SplitFrom,
                tools = cell.Tools,
                jobs = cell.Jobs.Keys.ToList(),
                models = cell.Models.Select(m => new { alias = m.Key, provider = m.Value.Provider, model = m.Value.Model }).ToList(),
                memoryDomains = cell.MemoryProfile?.Domains,
                lineageTags = cell.MemoryProfile?.LineageTags,
                divisionEvents = divEvents.Select(d => new
                {
                    at = d.OccurredAt,
                    parent = d.ParentWorkflow,
                    children = d.Children,
                    reason = d.Reason,
                    approvedBy = d.ApprovedBy
                }).ToList()
            });
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  Cell: {cell.Name}  [{cell.Domain}]");
        if (cell.SplitFrom is not null)
            Console.WriteLine($"  Split from: {cell.SplitFrom}");
        Console.WriteLine();

        if (cell.Tools.Count > 0)
            Console.WriteLine($"  Tools:  {string.Join(", ", cell.Tools)}");
        if (cell.Jobs.Count > 0)
            Console.WriteLine($"  Jobs:   {string.Join(", ", cell.Jobs.Keys)}");
        if (cell.Models.Count > 0)
            Console.WriteLine($"  Models: {string.Join(", ", cell.Models.Select(m => $"{m.Key}={m.Value.Provider}/{m.Value.Model}"))}");
        if (cell.MemoryProfile is not null)
            Console.WriteLine($"  Memory: [{string.Join(", ", cell.MemoryProfile.Domains)}]");

        if (divEvents.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Division history:");
            foreach (var d in divEvents)
            {
                var role = d.ParentWorkflow.Equals(name, StringComparison.OrdinalIgnoreCase) ? "parent" : "child";
                Console.WriteLine($"    {d.OccurredAt:yyyy-MM-dd HH:mm} UTC  [{role}]  {d.ParentWorkflow} → [{string.Join(", ", d.Children)}]  ({d.Reason})");
            }
        }
        Console.WriteLine();
    }
}

using Ananke.Organics.Kernel.Snapshots;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke kernel history &lt;file&gt;</c> — displays the division
/// timeline showing parent → children lineage from a mesh snapshot.
/// </summary>
internal static class KernelHistoryCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to a kernel snapshot YAML file."
        };

        var command = new Command("history", "Show division history and lineage from a kernel snapshot.")
        {
            fileArg
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var json = parseResult.GetValue<bool>("--json");
            return Execute(file, json);
        });

        return command;
    }

    private static int Execute(FileInfo file, bool json)
    {
        if (!file.Exists)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"File not found: {file.FullName}" });
            else
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            return 1;
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
            return 1;
        }

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                kernel = snapshot.KernelId,
                divisionCount = snapshot.DivisionHistory.Count,
                divisions = snapshot.DivisionHistory.Select(d => new
                {
                    parent = d.ParentWorkflow,
                    children = d.Children,
                    reason = d.Reason,
                    occurredAt = d.OccurredAt,
                    approvedBy = d.ApprovedBy
                })
            });
            return 0;
        }

        // Human-readable output
        Console.WriteLine();
        Console.WriteLine($"  Kernel: {snapshot.KernelId}  (v{snapshot.Version})");

        if (snapshot.DivisionHistory.Count == 0)
        {
            Console.WriteLine("  No division history — kernel is still in its genesis state.");
            Console.WriteLine();
            return 0;
        }

        Console.WriteLine($"  Divisions: {snapshot.DivisionHistory.Count}");
        Console.WriteLine();

        for (var i = 0; i < snapshot.DivisionHistory.Count; i++)
        {
            var record = snapshot.DivisionHistory[i];
            var approver = record.ApprovedBy is not null ? $"  (approved by {record.ApprovedBy})" : "";
            Console.WriteLine($"  {i + 1}. {record.OccurredAt:yyyy-MM-dd HH:mm} UTC{approver}");
            Console.WriteLine($"     {record.ParentWorkflow} → {string.Join(" + ", record.Children)}");
            Console.WriteLine($"     Reason: {record.Reason}");
            Console.WriteLine();
        }

        // Show current lineage tree
        Console.WriteLine("  Lineage:");
        var roots = snapshot.Cells.Where(c => c.SplitFrom is null).ToList();
        var childrenByParent = snapshot.Cells
            .Where(c => c.SplitFrom is not null)
            .GroupBy(c => c.SplitFrom!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Also include dead parents from history that have living descendants
        var allHistoryParents = snapshot.DivisionHistory
            .Select(d => d.ParentWorkflow)
            .Distinct()
            .Where(p => !snapshot.Cells.Any(c => c.Name == p))
            .ToList();

        if (roots.Count == 0 && allHistoryParents.Count > 0)
        {
            // All roots have been divided — trace from history
            foreach (var parent in allHistoryParents.Where(p =>
                !snapshot.DivisionHistory.Any(d => d.Children.Contains(p))))
            {
                PrintLineage(parent, childrenByParent, snapshot, indent: 4, isDead: true);
            }
        }
        else
        {
            foreach (var root in roots)
                PrintLineage(root.Name, childrenByParent, snapshot, indent: 4, isDead: false);
        }

        Console.WriteLine();

        return 0;
    }

    private static void PrintLineage(
        string name,
        Dictionary<string, List<WorkflowSnapshot>> childrenByParent,
        HostSnapshot snapshot,
        int indent,
        bool isDead)
    {
        var prefix = new string(' ', indent);
        var alive = snapshot.Cells.Any(c => c.Name == name);
        var marker = alive ? "●" : "✝";
        var domain = snapshot.Cells.FirstOrDefault(c => c.Name == name)?.Domain;
        var domainLabel = domain is not null ? $"  [{domain}]" : "";

        Console.WriteLine($"{prefix}{marker} {name}{domainLabel}");

        if (childrenByParent.TryGetValue(name, out var children))
        {
            foreach (var child in children)
                PrintLineage(child.Name, childrenByParent, snapshot, indent + 2, isDead: false);
        }

        // Also check history for dead children that divided further
        var historyChildren = snapshot.DivisionHistory
            .Where(d => d.ParentWorkflow == name)
            .SelectMany(d => d.Children)
            .Where(c => !snapshot.Cells.Any(cell => cell.Name == c && cell.SplitFrom == name))
            .Distinct();

        foreach (var deadChild in historyChildren)
        {
            if (!snapshot.Cells.Any(c => c.Name == deadChild))
                PrintLineage(deadChild, childrenByParent, snapshot, indent + 2, isDead: true);
        }
    }
}

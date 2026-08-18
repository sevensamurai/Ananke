using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Tool.Shared;
using System.CommandLine;
using System.Text.Json;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke mesh lineage &lt;cell&gt; &lt;file&gt;</c> — renders an ASCII tree
/// showing the founding ancestor, lineage to the named cell, and all descendants.
/// Supports reading from a <see cref="HostSnapshot"/> YAML file or a lineage store
/// JSON export.
/// </summary>
internal static class LineageCommand
{
    public static Command Create()
    {
        var cellArg = new Argument<string>("cell")
        {
            Description = "Cell name to show lineage for."
        };

        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to a host snapshot YAML file or lineage store JSON export."
        };

        var command = new Command("lineage",
            "Show ancestor/descendant tree for a cell. Reads from a host snapshot or lineage JSON export.")
        {
            cellArg,
            fileArg
        };

        command.SetAction(parseResult =>
        {
            var cell = parseResult.GetValue(cellArg)!;
            var file = parseResult.GetValue(fileArg)!;
            var json = parseResult.GetValue<bool>("--json");
            return Execute(cell, file, json);
        });

        return command;
    }

    private static int Execute(string cellName, FileInfo file, bool json)
    {
        if (!file.Exists)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"File not found: {file.FullName}" });
            else
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            return 1;
        }

        // Build lineage records from snapshot division history
        var text = File.ReadAllText(file.FullName);
        IReadOnlyList<CellLineage> records;

        try
        {
            records = LoadLineage(text, file.Extension);
        }
        catch (Exception ex)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Failed to parse file: {ex.Message}" });
            else
                Console.Error.WriteLine($"  Failed to parse file: {ex.Message}");
            return 1;
        }

        var target = records.FirstOrDefault(r =>
            r.CellId.Equals(cellName, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            var available = string.Join(", ", records.Select(r => r.CellId).Take(10));
            if (json)
                JsonOutput.Write(new { status = "not_found", cell = cellName, available });
            else
                Console.Error.WriteLine($"  Cell '{cellName}' not found. Available: {available}");
            return 1;
        }

        // Collect ancestors + the cell itself
        var ancestors = BuildAncestorChain(cellName, records);
        // Collect all descendants
        var descendants = BuildDescendantTree(cellName, records);

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                cell = cellName,
                ancestors = ancestors.Select(r => new
                {
                    cellId = r.CellId,
                    workflowName = r.WorkflowName,
                    generation = r.Generation,
                    parentCellId = r.ParentCellId,
                    bornAt = r.BornAt,
                    diedAt = r.DiedAt,
                    deathReason = r.DeathReason,
                    divisionReason = r.DivisionReason
                }).ToList(),
                descendants = FlattenTree(descendants).Select(r => new
                {
                    cellId = r.CellId,
                    workflowName = r.WorkflowName,
                    generation = r.Generation,
                    parentCellId = r.ParentCellId,
                    bornAt = r.BornAt,
                    diedAt = r.DiedAt,
                    deathReason = r.DeathReason
                }).ToList()
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  Lineage for: {cellName}");
        Console.WriteLine();

        // Print ancestor chain top-down
        for (var i = 0; i < ancestors.Count; i++)
        {
            var r = ancestors[i];
            var prefix = i == 0 ? "  ◉" : "  " + new string(' ', i * 2) + "└─";
            var dead = r.DiedAt.HasValue ? $"  (†{r.DiedAt:yyyy-MM-dd}  {r.DeathReason})" : "";
            var current = r.CellId.Equals(cellName, StringComparison.OrdinalIgnoreCase) ? "  ← YOU" : "";
            Console.WriteLine($"{prefix} {r.CellId}  gen={r.Generation}{dead}{current}");
        }

        // Print descendants as a tree
        if (descendants.Count > 0)
        {
            PrintDescendantTree(descendants, ancestors.Count * 2 + 2);
        }

        Console.WriteLine();

        return 0;
    }

    private static IReadOnlyList<CellLineage> LoadLineage(string text, string extension)
    {
        // Try JSON lineage export first, then fall back to HostSnapshot YAML
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Deserialize<List<CellLineage>>(text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }

        // HostSnapshot YAML — derive lineage from DivisionHistory
        var snapshot = HostSnapshotExporter.FromYaml(text);
        return DeriveLineageFromSnapshot(snapshot);
    }

    private static IReadOnlyList<CellLineage> DeriveLineageFromSnapshot(HostSnapshot snapshot)
    {
        var records = new List<CellLineage>();
        var allCellNames = snapshot.Cells.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Add alive cells
        foreach (var cell in snapshot.Cells)
        {
            var parentId = cell.SplitFrom;
            var gen = parentId is null ? 0
                : (records.FirstOrDefault(r => r.CellId.Equals(parentId, StringComparison.OrdinalIgnoreCase))?.Generation + 1 ?? 1);

            records.Add(new CellLineage
            {
                CellId = cell.Name,
                WorkflowName = cell.Name,
                ParentCellId = parentId,
                Generation = gen,
                BornAt = snapshot.TakenAt
            });
        }

        // Add dead parents from division history that are no longer alive
        foreach (var div in snapshot.DivisionHistory)
        {
            if (allCellNames.Contains(div.ParentWorkflow)) continue;
            if (records.Any(r => r.CellId.Equals(div.ParentWorkflow, StringComparison.OrdinalIgnoreCase))) continue;

            records.Add(new CellLineage
            {
                CellId = div.ParentWorkflow,
                WorkflowName = div.ParentWorkflow,
                Generation = 0,
                BornAt = div.OccurredAt - TimeSpan.FromHours(1),
                DiedAt = div.OccurredAt,
                DeathReason = "divided"
            });
        }

        return records;
    }

    private static List<CellLineage> BuildAncestorChain(string cellId, IReadOnlyList<CellLineage> records)
    {
        var chain = new List<CellLineage>();
        var current = records.FirstOrDefault(r => r.CellId.Equals(cellId, StringComparison.OrdinalIgnoreCase));
        while (current is not null)
        {
            chain.Insert(0, current);
            current = current.ParentCellId is null ? null
                : records.FirstOrDefault(r => r.CellId.Equals(current.ParentCellId, StringComparison.OrdinalIgnoreCase));
        }
        return chain;
    }

    private record TreeNode(CellLineage Lineage, List<TreeNode> Children);

    private static List<TreeNode> BuildDescendantTree(string cellId, IReadOnlyList<CellLineage> records)
    {
        return records
            .Where(r => r.ParentCellId?.Equals(cellId, StringComparison.OrdinalIgnoreCase) == true)
            .Select(r => new TreeNode(r, BuildDescendantTree(r.CellId, records)))
            .ToList();
    }

    private static IEnumerable<CellLineage> FlattenTree(IReadOnlyList<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node.Lineage;
            foreach (var d in FlattenTree(node.Children))
                yield return d;
        }
    }

    private static void PrintDescendantTree(List<TreeNode> nodes, int baseIndent, bool isLast = false)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var last = i == nodes.Count - 1;
            var connector = last ? "└─" : "├─";
            var indent = new string(' ', baseIndent);
            var dead = node.Lineage.DiedAt.HasValue ? $"  (†{node.Lineage.DiedAt:yyyy-MM-dd}  {node.Lineage.DeathReason})" : "";
            Console.WriteLine($"  {indent}{connector} {node.Lineage.CellId}  gen={node.Lineage.Generation}{dead}");
            PrintDescendantTree(node.Children, baseIndent + 2, last);
        }
    }
}

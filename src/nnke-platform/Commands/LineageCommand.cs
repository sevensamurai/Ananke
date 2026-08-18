using Ananke.Organics.Kernel.Snapshots;
using Ananke.Tool.Shared;
using System.CommandLine;
using System.Text.Json;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform lineage &lt;cell&gt; &lt;file&gt;</c> — shows the
/// ancestor/descendant tree for a named cell from a host snapshot, annotated
/// with deployment ID and platform where available.
/// </summary>
internal static class LineageCommand
{
    public static Command Create()
    {
        var cellArg = new Argument<string>("cell") { Description = "Cell name to show lineage for." };

        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to a host snapshot YAML file."
        };

        var command = new Command("lineage",
            "Show the ancestor/descendant lineage tree for a cell, annotated with platform info.")
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
        if (!SnapshotLoader.TryLoad(file, out var snapshot, out var loadError))
        {
            if (json) JsonOutput.Write(new { status = "error", message = loadError });
            else Console.Error.WriteLine($"  {loadError}");
            return 1;
        }

        var lineageMap = snapshot.Cells.ToDictionary(c => c.Name, c => c.SplitFrom, StringComparer.OrdinalIgnoreCase);

        if (!lineageMap.ContainsKey(cellName))
        {
            if (json) JsonOutput.Write(new { status = "not_found", cellName, available = lineageMap.Keys.ToList() });
            else Console.Error.WriteLine($"  Cell '{cellName}' not found in snapshot.");
            return 1;
        }

        List<string> Ancestors(string id)
        {
            var chain = new List<string>();
            var cur = id;
            while (cur is not null)
            {
                chain.Insert(0, cur);
                cur = lineageMap.TryGetValue(cur, out var p) ? p : null;
            }
            return chain;
        }

        List<string> Descendants(string id)
        {
            var result = new List<string>();
            foreach (var (k, v) in lineageMap)
                if (v?.Equals(id, StringComparison.OrdinalIgnoreCase) == true)
                { result.Add(k); result.AddRange(Descendants(k)); }
            return result;
        }

        var ancestors = Ancestors(cellName);
        var descendants = Descendants(cellName);

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                kernelId = snapshot.KernelId,
                cell = cellName,
                ancestors,
                descendants,
                note = "Platform and deploymentId annotations require a live IDeploymentRegistry connection (v0.9)."
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  Colony: {snapshot.KernelId}  —  lineage for: {cellName}");
        Console.WriteLine();

        for (var i = 0; i < ancestors.Count; i++)
        {
            var prefix = i == 0 ? "  ◉" : "  " + new string(' ', i * 2) + "└─";
            var current = ancestors[i].Equals(cellName, StringComparison.OrdinalIgnoreCase) ? "  ← target" : "";
            Console.WriteLine($"{prefix} {ancestors[i]}{current}");
        }

        foreach (var d in descendants)
        {
            var indent = new string(' ', ancestors.Count * 2 + 2);
            Console.WriteLine($"  {indent}└─ {d}");
        }

        Console.WriteLine();
        Console.WriteLine("  Note: platform/deploymentId annotations require nnke-platform connected to a registry (v0.9).");
        Console.WriteLine();

        return 0;
    }
}

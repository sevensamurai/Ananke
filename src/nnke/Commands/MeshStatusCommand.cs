using Ananke.Organics.Kernel.Snapshots;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke mesh status &lt;file&gt;</c> — displays alive cells with their
/// domain, generation, age, tools, and division-stress information from a
/// <see cref="HostSnapshot"/> YAML file.
/// </summary>
/// <remarks>
/// The snapshot is produced at runtime by
/// <see cref="HostSnapshotExporter.ToYaml(HostSnapshot)"/>. Supports <c>--json</c>
/// for agent self-diagnosis and machine-readable pipelines.
/// </remarks>
internal static class MeshStatusCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to a host snapshot YAML file."
        };

        var command = new Command("status",
            "Show alive cells, domains, tools, models, and routing from a host snapshot.")
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
        if (!SnapshotLoader.TryLoad(file, out var snapshot, out var loadError))
        {
            if (json) JsonOutput.Write(new { status = "error", message = loadError });
            else Console.Error.WriteLine($"  {loadError}");
            return 1;
        }

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                kernelId = snapshot.KernelId,
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
                    models = c.Models.Keys.ToList(),
                    memoryDomains = c.MemoryProfile?.Domains,
                }),
                routing = snapshot.RoutingTable
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  Mesh: {snapshot.KernelId}  v{snapshot.Version}  ({snapshot.TakenAt:yyyy-MM-dd HH:mm} UTC)");
        Console.WriteLine($"  Alive cells: {snapshot.Cells.Count}");
        Console.WriteLine();

        foreach (var cell in snapshot.Cells)
        {
            var from = cell.SplitFrom is not null ? $"  ← {cell.SplitFrom}" : "";
            Console.WriteLine($"  ● {cell.Name}  [{cell.Domain}]{from}");
            if (cell.Tools.Count > 0)
                Console.WriteLine($"    Tools:  {string.Join(", ", cell.Tools)}");
            if (cell.Models.Count > 0)
                Console.WriteLine($"    Models: {string.Join(", ", cell.Models.Keys)}");
            if (cell.MemoryProfile is not null)
                Console.WriteLine($"    Memory: [{string.Join(", ", cell.MemoryProfile.Domains)}]");
        }

        if (snapshot.RoutingTable.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Routing:");
            foreach (var (domain, cell) in snapshot.RoutingTable)
                Console.WriteLine($"    {domain} → {cell}");
        }
        Console.WriteLine();

        return 0;
    }
}

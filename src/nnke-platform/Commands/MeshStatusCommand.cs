using Ananke.Organics.Kernel.Snapshots;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform mesh &lt;file&gt;</c> — extends the local
/// mesh status view with platform, deployment ID, and remote health columns.
/// In v0.8.0 the remote health column is stubbed; live registry wiring is v0.9.
/// </summary>
internal static class MeshStatusCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to a host snapshot YAML file."
        };

        var command = new Command("mesh",
            "Show alive cells with platform, deploymentId, and remote health. " +
            "For scaffolding/inspection, use nnke mesh status.")
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
                    platform = (string?)null,
                    deploymentId = (string?)null,
                    remoteHealth = "not-connected"
                }),
                routing = snapshot.RoutingTable,
                note = "Platform/deploymentId/remoteHealth require live IDeploymentRegistry (v0.9)."
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  Mesh: {snapshot.KernelId}  v{snapshot.Version}  ({snapshot.TakenAt:yyyy-MM-dd HH:mm} UTC)");
        Console.WriteLine($"  Alive cells: {snapshot.Cells.Count}");
        Console.WriteLine();
        Console.WriteLine($"  {"Cell",-25} {"Domain",-18} {"Tools",-6} {"Platform",-12} {"Remote Health"}");
        Console.WriteLine($"  {new string('─', 80)}");

        foreach (var cell in snapshot.Cells)
        {
            Console.WriteLine($"  {cell.Name,-25} {cell.Domain,-18} {cell.Tools.Count,-6} {"(local)",-12} not-connected");
        }

        Console.WriteLine();
        Console.WriteLine("  ⚠  Platform, deployment ID, and remote health require a live registry connection.");
        Console.WriteLine("     Run 'nnke-platform login' to configure credentials, then redeploy.");
        Console.WriteLine();

        return 0;
    }
}

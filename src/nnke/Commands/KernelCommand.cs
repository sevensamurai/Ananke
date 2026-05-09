using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke kernel</c> — runtime observability commands for organic
/// workflow kernels. Reads mesh state from a snapshot YAML file produced
/// by <see cref="Ananke.Organics.Kernel.Snapshots.HostSnapshotExporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// A mesh is a set of running workflow cells that grow and divide organically.
/// These commands inspect the mesh's current topology and history — they do
/// <b>not</b> manage cell lifecycle (that's the host's job at runtime).
/// </para>
/// </remarks>
internal static class KernelCommand
{
    public static Command Create()
    {
        var command = new Command("kernel",
            "Inspect organic workflow kernels — view active cells, domains, tools, and division history.");

        command.Add(KernelStatusCommand.Create());
        command.Add(KernelHistoryCommand.Create());

        return command;
    }
}

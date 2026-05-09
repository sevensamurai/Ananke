using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Groups runtime observability commands under <c>nnke mesh</c>.
/// All subcommands operate on file-backed or in-process state without requiring
/// cloud SDK dependencies — consistent with the lean <c>nnke</c> footprint.
/// </summary>
/// <remarks>
/// For federation-level mesh management (deploy, teardown, remote health),
/// use <c>nnke-platform mesh</c> instead.
/// </remarks>
internal static class MeshCommand
{
    public static Command Create()
    {
        var command = new Command("mesh",
            "Inspect organic mesh state — cells, lineage, memory, and signals. " +
            "For federation ops, use nnke-platform.");

        command.Add(MeshStatusCommand.Create());
        command.Add(CellTraceCommand.Create());
        command.Add(MemoryInspectCommand.Create());
        command.Add(LineageCommand.Create());

        return command;
    }
}

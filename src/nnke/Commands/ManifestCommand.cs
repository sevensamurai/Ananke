using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Groups manifest-centric subcommands under <c>nnke manifest</c>.
/// These commands operate on <c>.ananke.yml</c> files: validation and diagram export.
/// </summary>
/// <remarks>
/// <para>
/// The top-level <c>nnke validate</c> and <c>nnke diagram</c> commands remain
/// as deprecated aliases for one release to preserve backwards compatibility
/// with existing scripts and documentation.
/// </para>
/// </remarks>
internal static class ManifestCommand
{
    public static Command Create()
    {
        var command = new Command("manifest",
            "Work with .ananke.yml manifest files — validate topology, export diagrams.");

        command.Add(ValidateCommand.Create("validate"));
        command.Add(DiagramCommand.Create("diagram"));

        return command;
    }
}

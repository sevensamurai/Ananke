using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Groups scaffolding subcommands under <c>dotnet ananke new</c>.
/// </summary>
internal static class NewCommand
{
    public static Command Create()
    {
        var command = new Command("new", "Scaffold new Ananke projects and files.");

        command.Add(NewWorkflowCommand.Create());
        command.Add(NewManifestCommand.Create());

        return command;
    }
}

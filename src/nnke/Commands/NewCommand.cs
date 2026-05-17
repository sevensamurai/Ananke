using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Groups scaffolding subcommands under <c>nnke new</c>.
/// </summary>
internal static class NewCommand
{
    public static Command Create()
    {
        var command = new Command("new", "Scaffold a new Ananke project.")
        {
            NewQuickstartCommand.Create(),
            NewWorkflowCommand.Create(),
            NewChatboxCommand.Create(),
            NewPatternCommand.Create()
        };

        return command;
    }
}

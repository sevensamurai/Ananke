using Ananke.Tool.Commands;
using Ananke.Tool.Shared;
using System.CommandLine;

var jsonOption = CliOptions.CreateJsonOption();

var rootCommand = new RootCommand("Ananke CLI — scaffold working AI agent projects from zero. Run 'nnke new quickstart <name>' to start.")
{
    jsonOption,
    NewCommand.Create(),
    ManifestCommand.Create(),
    ValidateCommand.Create(),
    DiagramCommand.Create(),
    ServeCommand.Create(),
    InspectCommand.Create(),
    ExplainCommand.Create(),
    PatternsCommand.Create(),
    DocsCommand.Create(),
    McpServerCommand.Create(),
    MeshCommand.Create()
};
rootCommand.Add(SchemaCommand.Create(rootCommand));
rootCommand.Add(KernelCommand.Create());

try
{
    return await rootCommand.Parse(args).InvokeAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Console.Error.WriteLine($"  ✗ Unexpected error: {ex.Message}");
    return 1;
}

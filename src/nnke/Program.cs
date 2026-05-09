using Ananke.Tool.Commands;
using Ananke.Tool.Shared;
using System.CommandLine;

var jsonOption = CliOptions.CreateJsonOption();

var rootCommand = new RootCommand("Ananke CLI — scaffold workflows, validate manifests, and export diagrams. For federation ops, install nnke-platform.")
{
    jsonOption,
    NewCommand.Create(),
    ValidateCommand.Create(),
    RunCommand.Create(),
    ServeCommand.Create(),
    DiagramCommand.Create(),
    InspectCommand.Create(),
    ExplainCommand.Create(),
    PatternsCommand.Create(),
    DocsCommand.Create(),
    McpServerCommand.Create(),
    MeshCommand.Create()
};
rootCommand.Add(SchemaCommand.Create(rootCommand));
rootCommand.Add(KernelCommand.Create());

return await rootCommand.Parse(args).InvokeAsync();

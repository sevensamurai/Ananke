using Ananke.Tool.Commands;
using System.CommandLine;

var jsonOption = new Option<bool>("--json")
{
    Description = "Emit machine-readable JSON output instead of human-formatted text.",
    Recursive = true
};

var rootCommand = new RootCommand("Ananke CLI — scaffold workflows, validate manifests, and export diagrams.")
{
    jsonOption
};

rootCommand.Add(NewCommand.Create());
rootCommand.Add(ValidateCommand.Create());
rootCommand.Add(DiagramCommand.Create());
rootCommand.Add(InspectCommand.Create());
rootCommand.Add(ExplainCommand.Create());
rootCommand.Add(PatternsCommand.Create());
rootCommand.Add(DocsCommand.Create());
rootCommand.Add(McpServerCommand.Create());
rootCommand.Add(SchemaCommand.Create(rootCommand));

return await rootCommand.Parse(args).InvokeAsync();

using Ananke.Tool.Platform;
using Ananke.Tool.Platform.Commands;
using Ananke.Tool.Shared;
using System.CommandLine;

var jsonOption = CliOptions.CreateJsonOption();

var inMemoryOption = new Option<bool>("--in-memory")
{
    Description = "Use an ephemeral in-memory deployment registry instead of the default file-backed one. Useful for testing.",
    Recursive = true
};

var rootCommand = new RootCommand("Ananke Platform CLI — validate, deploy, and manage workflow federation to cloud platforms. For scaffolding and inspection, use nnke.")
{
    jsonOption,
    inMemoryOption,
    ValidateCommand.Create(),
    CapabilitiesCommand.Create(),
    EvalCommand.Create(),
    ProfilesCommand.Create(),
    DeployCommand.Create(),
    StatusCommand.Create(),
    TeardownCommand.Create(),
    TrendsCommand.Create(),
    AnalyzeCommand.Create(),
    LineageCommand.Create(),
    MeshStatusCommand.Create(),
    ApoptosisCommand.Create(),
    CompareCommand.Create(),
    EventsCommand.Create(),
    LoginCommand.Create(),
    WhoAmICommand.Create(),
    AdaptersCommand.Create()
};

return await rootCommand.Parse(args).InvokeAsync();

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

// ── The 1.0 surface ──────────────────────────────────────────────────────────
//
// Deliberately smaller than what this CLI used to expose, because *removing a verb after 1.0 is
// breaking and adding one is not*). Everything registered below
// can return a true answer today.
//
// Not registered, and why — each can be added back without breaking anyone:
//
//   trends, compare, events   RemoteMetricsTracker is per-process and every command constructs a
//                             fresh one, so these could only ever report "no data" and exit 0 —
//                             indistinguishable from a healthy nothing-to-report. They need a
//                             metrics store that does not exist.
//   apoptosis                 Identifies and tears down idle cells; needs a real substrate
//                             population to act on, which nothing yet produces.
//
// The command classes are kept and still tested; only the registration is withdrawn.
var rootCommand = new RootCommand("Ananke Platform CLI — validate, deploy, and manage workflow federation to cloud platforms. For scaffolding and inspection, use nnke.")
{
    jsonOption,
    inMemoryOption,
    ValidateCommand.Create(),
    CheckCommand.Create(),
    CapabilitiesCommand.Create(),
    EvalCommand.Create(),
    ProfilesCommand.Create(),
    DeployCommand.Create(),
    StatusCommand.Create(),
    TeardownCommand.Create(),
    AnalyzeCommand.Create(),
    LineageCommand.Create(),
    MeshStatusCommand.Create(),
    LoginCommand.Create(),
    WhoAmICommand.Create(),
    AdaptersCommand.Create()
};

return await rootCommand.Parse(args).InvokeAsync();

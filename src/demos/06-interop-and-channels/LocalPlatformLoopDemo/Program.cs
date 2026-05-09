using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Execution;
using Ananke.Federation.LocalEmulators;
using Ananke.Federation.Recommendation;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;

// ─────────────────────────────────────────────────────────────────────────────
//  LocalPlatformLoopDemo
//
//  Demonstrates the local design loop: a workflow that declares three
//  platform-native capabilities (code_execution, web_search, memory_bank)
//  is validated and run against three targets without credentials or network
//  access to any cloud platform:
//
//    Target 1 — local-emulated:azure-ai
//    Target 2 — local-emulated:claude
//    Target 3 — local-emulated:vertex-ai  (also shown via the "foundry" alias)
//
//  Key types:
//    PlatformNativeExecutorRegistry  — maps capability names to local executors
//    DefaultPlatformNativeExecutors  — registers all built-in emulators
//    LocalPlatformValidator          — validates coverage for a target platform
//    LocalFederationDeployer         — deploys in-process, no credentials needed
//
//  No API keys. No cloud SDKs. No network required for the core loop.
//  (web_search / web_fetch emulators do make HTTP calls if you let them run.)
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("══════════════════════════════════════════════════════════════════");
Console.WriteLine("  LocalPlatformLoopDemo — Ananke local design loop");
Console.WriteLine("══════════════════════════════════════════════════════════════════");
Console.WriteLine();

// ── 1. Build a ToolKit with three platform-native capabilities ────────────────
//
//  These tools have NO local execute delegate — they rely entirely on a
//  registered PlatformNativeExecutor to be useful at runtime.
//  OnExecute is provided only as a safe fallback (returns a stub message)
//  so the ToolKit is valid even before emulators are applied.
//  A fresh ToolKit is built per-target so ReplaceExecutor patches don't bleed
//  across runs (see BuildToolKit() at the bottom of this file).

var toolKit = BuildToolKit();

// ── Helper: build a fresh ToolKit with the three platform-native tools ────────
static ToolKit BuildToolKit() =>
    new ToolKit("local-platform-loop")
        .AddTool("run_code", "Execute a code snippet on the target platform", b => b
            .Param<string>("language", "Programming language (python, javascript, bash)")
            .Param<string>("code", "The code to execute")
            .PlatformNative("code_execution")
            .OnExecute(_ => ToolResult.Ok("[local fallback — no emulator applied]")))
        .AddTool("search_web", "Search the web for information", b => b
            .Param<string>("query", "Search query")
            .PlatformNative("web_search")
            .OnExecute(_ => ToolResult.Ok("[local fallback — no emulator applied]")))
        .AddTool("store_memory", "Persist a key/value entry in the platform memory bank", b => b
            .Param<string>("key", "Memory key")
            .Param<string>("value", "Value to store")
            .PlatformNative("memory_bank")
            .OnExecute(_ => ToolResult.Ok("[local fallback — no emulator applied]")));

// ── 2. Build the emulator registry once — shared across all targets ───────────

var registry = new PlatformNativeExecutorRegistry();
DefaultPlatformNativeExecutors.Register(registry);

Console.WriteLine($"Registered {registry.RegisteredKeys.Count} capability executors.");
Console.WriteLine();

// ── 3. Load the manifest ──────────────────────────────────────────────────────

var manifestPath = Path.Combine(AppContext.BaseDirectory, "local-platform-loop.ananke.yml");
var manifest = WorkflowManifest.Load(manifestPath);
Console.WriteLine($"Manifest loaded: '{manifest.Name}'  ({manifest.Jobs.Count} jobs)");
Console.WriteLine();

// ── 4. Run the design loop against three targets ──────────────────────────────

var targets = new[]
{
    ("local-emulated:azure-ai",   "Azure AI (Foundry)"),
    ("local-emulated:claude",     "Anthropic Claude"),
    ("local-emulated:vertex-ai",  "Google Vertex AI"),
};

var deploymentRegistry = new InMemoryDeploymentRegistry();

foreach (var (target, label) in targets)
{
    Console.WriteLine($"── Target: {label} ({target}) ──────────────────────────────────");

    // 4a. Validate capability coverage for this emulated platform
    var emulatedPlatform = target.StartsWith("local-emulated:", StringComparison.Ordinal)
        ? target["local-emulated:".Length..]
        : target;

    var validator = new LocalPlatformValidator(registry, emulatedPlatform: emulatedPlatform);
    var report = await validator.ValidateAsync(manifest, toolKit);

    if (report.Diagnostics.Count == 0)
    {
        Console.WriteLine("  ✅ Validation passed — all capabilities covered");
    }
    else
    {
        foreach (var d in report.Diagnostics)
            Console.WriteLine($"  {(d.Severity == DeployDiagnosticSeverity.Error ? "❌" : "⚠️")}  [{d.Code}] {d.Message}");
    }

    if (!report.IsDeployable)
    {
        Console.WriteLine("  Skipping deploy — validation errors present.");
        Console.WriteLine();
        continue;
    }

    // 4b. Apply emulators to the ToolKit so tools execute locally.
    //     Build a fresh kit per-target so executor patches don't bleed across runs.
    var kitForTarget = BuildToolKit();
    var patched = registry.ApplyTo(kitForTarget, emulatedPlatform);
    Console.WriteLine($"  Patched {patched}/{kitForTarget.Tools.Count} tools with local emulators");

    // 4c. Deploy locally — no credentials, no network, in-process only
    var deployer = new LocalFederationDeployer(deploymentRegistry);
    var record = await deployer.DeployAsync(manifest, kitForTarget,
        new DeployOptions { Platform = "local" });

    Console.WriteLine($"  Deployed  id={record.DeploymentId[..8]}  status={record.Status}");

    // 4d. Run the workflow jobs using the patched ToolKit
    await RunWorkflowJobsAsync(kitForTarget, target);

    Console.WriteLine();
}

// ── 5. Show the foundry alias ─────────────────────────────────────────────────
//
//  "foundry" is a post-May-2026 alias for "azure-ai". The validator emits
//  FED060 (warning) so callers know the alias was resolved.

Console.WriteLine("── Platform alias demo ──────────────────────────────────────────");
var aliasValidator = new DeployabilityValidator();
var aliasReport = aliasValidator.Validate(manifest, BuildToolKit(), "foundry");
Console.WriteLine($"  'foundry' alias resolved → FED060 warnings: {aliasReport.Warnings.Count(d => d.Code == "FED060")}");
Console.WriteLine();

// ── 6. Platform recommendation ──────────────────────────────────────────────
//
//  Use PlatformRecommender to score all three candidates against the manifest
//  and toolkit without credentials or network access. Mirrors what
//  `nnke-platform eval` does from the CLI.

Console.WriteLine("── Platform recommendation ──────────────────────────────────────");

var recommender = new PlatformRecommender();
var fitReport = recommender.Evaluate(manifest, toolKit);

Console.WriteLine($"  Recommended: {fitReport.Recommended ?? "(none — all blocked)"}");
Console.WriteLine();
foreach (var score in fitReport.Scores)
{
    var blocked = score.Total == 0 && score.Reasons.Any(r => r.Kind == FitReasonKind.Block);
    var badge   = score.Platform == fitReport.Recommended ? " ★" : blocked ? " ⚠" : "";
    Console.WriteLine($"  {score.Platform,-22} {score.Total * 100,3:F0}%{badge}");
    foreach (var r in score.Reasons.Take(3))
        Console.WriteLine($"    {(r.Kind == FitReasonKind.Plus ? "+" : r.Kind == FitReasonKind.Block ? "✗" : "−")} {r.Message}");
}
Console.WriteLine();

// ── 7. Summary ────────────────────────────────────────────────────────────────

var allDeployments = await deploymentRegistry.ListAsync();
Console.WriteLine("══════════════════════════════════════════════════════════════════");
Console.WriteLine($"  Done. {allDeployments.Count} local deployment records created.");
Console.WriteLine("══════════════════════════════════════════════════════════════════");

// ─────────────────────────────────────────────────────────────────────────────
//  Helpers
// ─────────────────────────────────────────────────────────────────────────────

static async Task RunWorkflowJobsAsync(ToolKit kit, string target)
{
    // Drive the four manifest jobs in order using the patched ToolKit.
    // Each job invokes one platform-native tool to show the emulator tier in action.

    // Job: search — uses web_search emulator (real HTTP or stub)
    var searchResult = await kit.Tools["search_web"].ExecuteAsync(
        new Dictionary<string, object?> { ["query"] = "Ananke agent orchestration" });
    Console.WriteLine($"  [search]   {Truncate(searchResult.Value, 72)}");

    // Job: execute — uses code_execution emulator (bash subprocess)
    var execResult = await kit.Tools["run_code"].ExecuteAsync(
        new Dictionary<string, object?> { ["language"] = "python", ["code"] = "print(2 + 2)" });
    Console.WriteLine($"  [execute]  {Truncate(execResult.Value, 72)}");

    // Job: remember — uses memory_bank emulator (in-process ConcurrentDictionary)
    var memResult = await kit.Tools["store_memory"].ExecuteAsync(
        new Dictionary<string, object?> { ["key"] = $"demo:{target}", ["value"] = "ran" });
    Console.WriteLine($"  [remember] {Truncate(memResult.Value, 72)}");

    // Job: summarise (pure code, no platform tool needed)
    Console.WriteLine($"  [summarise] workflow complete for {target}");
}

static string Truncate(string? s, int max) =>
    s is null ? "(null)" : s.Length <= max ? s : s[..max] + "…";

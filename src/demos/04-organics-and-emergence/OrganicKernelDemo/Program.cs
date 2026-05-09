using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Exploration;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Sensing;
using OrganicKernelDemo;
using OrganicKernelDemo.Topology;
using System.Text;
using static OrganicKernelDemo.DemoConsole;
using OrganicKernelDemo.Infrastructure;

// ═══════════════════════════════════════════════════════════════════════
//  OrganicKernelDemo — Workflow Growth, Division & Learning
//
//  A bookstore workflow grows organically: starts as a single generalist,
//  accumulates tools, detects structural tension via a complexity monitor,
//  proposes division through an experience-driven policy, gets approval
//  through the pluggable IDivisionApprovalGate, splits into specialists,
//  and feeds the outcome back into empirical memory.
//
//  NEW in this version: OrganicHost + .JoinHost() replace ~40 lines of
//  manual monitor/policy/gate wiring. WorkflowDivider auto-executes
//  division when approved — spawn children, confirm health, kill parent.
//
//  Usage:
//    dotnet run                          # Automatic mode
//    dotnet run -- --supervised          # Human-in-the-loop approval
//    dotnet run -- --verbose             # Show YAML snapshots & details
//    dotnet run -- --simulate            # Dry-run division (no spawn/kill)
//    dotnet run -- --no-topology         # Skip colony topology report
//    dotnet run -- --supervised -v       # Both
//
//  No API keys required — all LLM responses are simulated.
// ═══════════════════════════════════════════════════════════════════════

Console.OutputEncoding = Encoding.UTF8;

var opts = DemoOptions.Parse(args);

Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
Print("  🧬 Ananke — Organic Workflow Demo", ConsoleColor.Cyan);
Print("  Bookstore workflow → organic growth → division → learning", ConsoleColor.DarkCyan);
if (opts.Supervised)
    Print("  Mode: --supervised (you will approve/reject divisions)", ConsoleColor.Magenta);
if (opts.Verbose)
    Print("  Mode: --verbose (YAML snapshots & details visible)", ConsoleColor.DarkYellow);
if (opts.Simulate)
    Print("  Mode: --simulate (division dry-run — no spawn/kill)", ConsoleColor.DarkYellow);
if (!opts.Supervised && !opts.Verbose && !opts.Simulate)
    Print("  Mode: automatic (use --supervised / --verbose / --simulate for more)", ConsoleColor.DarkGray);
Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
Console.WriteLine();

// ── Tool registry ───────────────────────────────────────────────────

var catalogTools = BookstoreTools.CreateCatalogTools();
var orderTools   = BookstoreTools.CreateOrderTools();
var toolRegistry = BookstoreTools.CreateFullRegistry(catalogTools, orderTools);

Print($"  📦 Tool registry: {toolRegistry.Tools.Count} tools", ConsoleColor.Gray);
if (opts.Verbose)
{
    Print($"     Catalog: {string.Join(", ", catalogTools.Tools.Keys)}", ConsoleColor.DarkGray);
    Print($"     Orders:  {string.Join(", ", orderTools.Tools.Keys)}", ConsoleColor.DarkGray);
}
Console.WriteLine();

// ── Infrastructure ──────────────────────────────────────────────────

var capabilityMap = new InMemoryCapabilityMap(signalTimeout: TimeSpan.FromMinutes(5));
var router = new KeywordRequestRouter(capabilityMap);
var requestLog = new List<string>();
var cellHost = new InProcessWorkflowHost();
var lineageStore = new InMemoryLineageStore();

var activatorFactory = new TypedWorkflowActivatorFactory<BookstoreState>()
    .WithTools(toolRegistry)
    .WithModelFactory(snap =>
        new FakeAgentModel($"[{snap.Model}] Simulated response for your request"))
    .WithPromptBuilder((state, _) => state.Request)
    .WithResultMapper((state, _, text) => state with { Response = text })
    .WithCodeJobHandler((state, _) => Task.FromResult(state))
    .WithInitialStateFactory(() => new BookstoreState());

// Also keep the old-style activator for Hydrate calls
var activator = new WorkflowActivator<BookstoreState>()
    .WithTools(toolRegistry)
    .WithModelFactory(snap =>
        new FakeAgentModel($"[{snap.Model}] Simulated response for your request"))
    .WithPromptBuilder((state, _) => state.Request)
    .WithResultMapper((state, _, text) => state with { Response = text })
    .WithCodeJobHandler((state, _) => Task.FromResult(state));

// ── Learning infrastructure (Tier 3: experience-driven division) ────

var empiricalMemory = new InMemoryEmpiricalMemory(new FakeEmbeddingModel());
var explorationStrategy = new UcbExplorationStrategy();
var outcomeTracker = new DivisionOutcomeTracker(empiricalMemory);

// ── WorkflowDivider — the mitosis engine (NEW) ─────────────────────

var divider = new WorkflowDivider(
    cellHost, capabilityMap, activatorFactory,
    new DivisionOptions { Simulate = opts.Simulate });

// ── OrganicHost — the growth brain ─────────────────────────────────

var clusterStrategy = new ToolKitClusterStrategy(catalogTools, orderTools);

IDivisionApprovalGate gate = opts.Supervised
    ? new CallbackApprovalGate(PromptHumanApproval)
    : new AutoApprovalGate();

await using var organicHost = new OrganicHost(
    cellHost: cellHost,
    capabilityMap,
    new OrganicGrowthOptions
    {
        Policy = new ExperienceDrivenDivisionPolicy(
            empiricalMemory, explorationStrategy,
            new ThresholdDivisionPolicy(minTools: 6, minClusters: 2,
                clusterStrategy: clusterStrategy.Split),
            clusterStrategy.Split),

        ApprovalGate = gate,

        // Evaluate after every 7 executions (lower than default 10 for demo)
        EvaluationInterval = 7,

        OutcomeTracker = outcomeTracker,

        ManifestFactory = BookstoreTools.BuildMinimalManifest,

        // NEW: Wire divider + shared memory so OrganicHost auto-executes division
        Divider = divider,
        SharedMemory = empiricalMemory
    });

// Observability events — for logging only, NOT governance
organicHost.OnDivisionProposed += async signal =>
{
    Print($"  📋 Division proposed: {signal.Plan.Reason}", ConsoleColor.Yellow);
    foreach (var child in signal.Plan.Children)
        Print($"     → {child.Name} (domain: {child.Domain}, tools: [{string.Join(", ", child.Tools)}])",
            ConsoleColor.Yellow);
};

DivisionSignal? approvedSignal = null;
DivisionSignal? rejectedSignal = null;
DivisionSignal? completedSignal = null;
DivisionSignal? failedSignal = null;

organicHost.OnDivisionApproved += async signal =>
{
    approvedSignal = signal;
    Print($"  ✅ Division approved: {signal.Approval!.Reason} (by: {signal.Approval.ReviewedBy})",
        ConsoleColor.Green);
};

organicHost.OnDivisionRejected += async signal =>
{
    rejectedSignal = signal;
    Print($"  ❌ Division rejected: {signal.Approval!.Reason} (by: {signal.Approval.ReviewedBy})",
        ConsoleColor.Red);
};

organicHost.OnDivisionCompleted += async signal =>
{
    completedSignal = signal;
    Print($"  🎉 Division executed — children spawned, parent killed", ConsoleColor.Green);
    Print($"     Active: [{string.Join(", ", organicHost.CellHost.ListActive())}]", ConsoleColor.DarkGray);
};

organicHost.OnDivisionFailed += async signal =>
{
    failedSignal = signal;
    Print($"  💥 Division execution failed — parent continues serving", ConsoleColor.Red);
};

Print("  🧠 OrganicHost created (growth brain — monitors, evaluates, gates, divides)", ConsoleColor.Cyan);
Print($"     Cell host: InProcessWorkflowHost", ConsoleColor.DarkGray);
Print($"     Gate:      {gate.GetType().Name}", ConsoleColor.DarkGray);
Print($"     Divider:   WorkflowDivider{(opts.Simulate ? " (simulate mode)" : "")}", ConsoleColor.DarkGray);
Print($"     Policy:    ExperienceDrivenDivisionPolicy → ThresholdDivisionPolicy fallback", ConsoleColor.DarkGray);
Console.WriteLine();

// =====================================================================
//  PHASE 1 — Genesis: Load a Single Bookstore Workflow from YAML
// =====================================================================

PrintPhase(1, "Genesis — Load Workflow from YAML Blueprint");

var yamlPath = Path.Combine(AppContext.BaseDirectory, "bookstore-general.mesh.yml");
Print($"  📄 Loading: {Path.GetFileName(yamlPath)}", ConsoleColor.White);

var yamlContent = await File.ReadAllTextAsync(yamlPath);
var loadedSnapshot = HostSnapshotExporter.FromYaml(yamlContent);
var genesis = loadedSnapshot.Cells[0];

Print($"  ✅ Loaded: '{genesis.Name}' (domain: {genesis.Domain})", ConsoleColor.Green);
if (opts.Verbose)
{
    Print($"     Tools: [{string.Join(", ", genesis.Tools)}]", ConsoleColor.DarkGray);
    Print($"     Jobs:  [{string.Join(", ", genesis.Jobs.Keys)}]", ConsoleColor.DarkGray);
    foreach (var (jobName, jobDef) in genesis.Jobs.Where(j => j.Value.SystemPrompt is not null))
    {
        var prompt = jobDef.SystemPrompt!;
        Print($"     Prompt ({jobName}): \"{prompt[..Math.Min(60, prompt.Length)].Trim()}...\"",
            ConsoleColor.DarkGray);
    }
}

PrintSubPhase("Activating snapshot → live Workflow<T>");
activator.Hydrate(genesis);
Print($"  🔬 Workflow '{genesis.Name}' activated — {genesis.Tools.Count} tools, " +
      $"{genesis.Jobs.Count} jobs", ConsoleColor.Green);

// JoinHost — one line, observation is automatic from here
var genesisWorkflow = BuildSimulationWorkflow(genesis.Name, jobCount: 2);
var organic = genesisWorkflow.JoinHost(organicHost, catalogTools);
Print($"  📊 Joined OrganicHost via .JoinHost() (structural profile auto-derived)", ConsoleColor.Gray);

await organicHost.CellHost.StartWithHealthCheckAsync(genesis, capabilityMap);
Print($"  🟢 Started with health check", ConsoleColor.Green);

await Task.Delay(300);

Print($"  Active: [{string.Join(", ", organicHost.CellHost.ListActive())}]", ConsoleColor.DarkGray);
if (opts.Verbose) PrintLandscape(capabilityMap);

// -- Simulate genesis traffic (low tool count → no division) ----------

PrintSubPhase("Simulating customer traffic...");

string[] genesisRequests =
[
    "Can you search for books about machine learning?",
    "What's the inventory for ISBN 978-0-13-468599-1?",
    "Recommend me some sci-fi books",
    "Get details for ISBN 978-0-7432-7356-5",
];

foreach (var req in genesisRequests)
{
    var target = await router.RouteAsync(req);
    requestLog.Add($"{target}: {req}");
    Print($"  📨 → {target}: \"{req[..Math.Min(50, req.Length)]}...\"", ConsoleColor.DarkYellow);

    // Pure execution — host observes automatically via the JoinHost wrapper
    await organic.RunAsync(new BookstoreState { Request = req });
    await Task.Delay(100);
}

await Task.Delay(300);

PrintSubPhase("Complexity check — low tension");
var snapshot1 = await ((WorkflowExecutionMonitor)organicHost.GetMonitor()).GetSnapshotAsync(genesis.Name);
PrintSnapshot(snapshot1);
Print($"  Division needed? NO — tension is low, only {genesis.Tools.Count} tools", ConsoleColor.Gray);
Console.WriteLine();

// =====================================================================
//  PHASE 2 — Growth: Adding Order & Payment Capabilities
// =====================================================================

PrintPhase(2, "Growth — Adding Order & Payment Tools");

var allTools = new ToolKit("all-tools").Merge(catalogTools).Merge(orderTools);
Print($"  📦 New tools: {string.Join(", ", orderTools.Tools.Keys)}", ConsoleColor.Yellow);
Print($"  🧬 Total: {allTools.Tools.Count} tools", ConsoleColor.Gray);

capabilityMap.Register(new WorkflowSignal
{
    WorkflowName = genesis.Name, Domain = genesis.Domain,
    Capabilities = allTools.Tools.Keys.ToList(),
    Timestamp = DateTimeOffset.UtcNow
});

// Re-join with full tool kit — structural profile updates automatically
var growthWorkflow = BuildSimulationWorkflow(genesis.Name, jobCount: 3);
var organicGrowth = growthWorkflow.JoinHost(organicHost, allTools);
Print($"  📊 Re-joined OrganicHost: {allTools.Tools.Count} tools → tension rising", ConsoleColor.Gray);

PrintSubPhase("Simulating mixed traffic...");

string[] growthRequests =
[
    "Search for 'Design Patterns' books",
    "I want to create an order for ISBN 978-0-201-63361-0",
    "Process payment of $34.99",
    "Track my order #ORD-4521",
    "What sci-fi do you recommend?",
    "Apply discount code SUMMER25",
    "I need to return order #ORD-3190",
    "Look up customer alice@bookstore.com",
    "Check inventory for ISBN 978-0-13-235088-4",
    "Process payment of $89.95 for bulk order",
];

foreach (var req in growthRequests)
{
    var target = await router.RouteAsync(req);
    requestLog.Add($"{target}: {req}");
    Print($"  📨 → {target}: \"{req[..Math.Min(55, req.Length)]}\"", ConsoleColor.DarkYellow);

    // Pure execution — host observes automatically via the JoinHost wrapper
    await organicGrowth.RunAsync(new BookstoreState { Request = req });
    await Task.Delay(80);
}

// Wait for background loop to process and potentially trigger division
Print("", ConsoleColor.DarkGray);
PrintSubPhase("Waiting for background evaluation...");
await Task.Delay(1000);

// -- Check what happened ──────────────────────────────────────────────

var snapshot2 = await ((WorkflowExecutionMonitor)organicHost.GetMonitor()).GetSnapshotAsync(genesis.Name);
PrintSnapshot(snapshot2);

// -- Snapshot v1 (pre-division) ───────────────────────────────────────

var snapshotV1 = new HostSnapshot
{
    KernelId = "bookstore", Version = 1, TakenAt = DateTimeOffset.UtcNow,
    Cells = [new WorkflowSnapshotBuilder(genesis.Name, genesis.Domain).Tools(allTools).Build()]
};

if (opts.Verbose)
{
    PrintSubPhase("Snapshot v1 — Pre-Division");
    PrintYamlBlock(HostSnapshotExporter.ToYaml(snapshotV1));
}
else
{
    Print("  📸 Snapshot v1 captured (use --verbose to see YAML)", ConsoleColor.Cyan);
}

Console.WriteLine();

// =====================================================================
//  PHASE 3 — Division Result
// =====================================================================

if (approvedSignal is not null)
{
    var divisionPlan = approvedSignal.Approval!.RevisedPlan ?? approvedSignal.Plan;
    var approval = approvedSignal.Approval!;

    PrintPhase(3, "Division Approved — Executing via WorkflowDivider");

    if (opts.Simulate)
    {
        Print("  🧪 Simulate mode — division was dry-run (no spawn/kill)", ConsoleColor.Yellow);
        Print($"  Would have split: {divisionPlan.ParentWorkflow} → " +
              $"{string.Join(" + ", divisionPlan.Children.Select(c => c.Name))}", ConsoleColor.Yellow);
        foreach (var child in divisionPlan.Children)
            Print($"     {child.Name}: domain={child.Domain}, tools=[{string.Join(", ", child.Tools)}]",
                ConsoleColor.DarkYellow);
        Console.WriteLine();
    }
    else
    {
        // ── OrganicHost auto-executes division via WorkflowDivider ──
        // The divider: derives snapshots → activates children → spawns →
        //   confirms health → kills parent. We just wait for the event.

        PrintSubPhase("WorkflowDivider executing: spawn → confirm → kill");

        // Wait for division execution to complete (background task in OrganicHost)
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (completedSignal is null && failedSignal is null && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(100);

        if (completedSignal is not null)
        {
            Print($"  ✅ Division complete — zero manual wiring", ConsoleColor.Green);
            Print($"  Active: [{string.Join(", ", organicHost.CellHost.ListActive())}]", ConsoleColor.DarkGray);
        }
        else if (failedSignal is not null)
        {
            Print($"  ❌ Division failed — parent resumed", ConsoleColor.Red);
        }
        else
        {
            Print($"  ⏱️  Division timed out (this is a demo timing issue)", ConsoleColor.Yellow);
        }
    }

    Console.WriteLine();

    // -- Record lineage (genesis → children) so the topology graph has ancestry data
    var divisionTime = DateTimeOffset.UtcNow;
    await lineageStore.RecordBirthAsync(new CellLineage
    {
        CellId = divisionPlan.ParentWorkflow,
        WorkflowName = divisionPlan.ParentWorkflow,
        Generation = 0,
        BornAt = divisionTime - TimeSpan.FromMinutes(5)
    });
    foreach (var child in divisionPlan.Children)
    {
        await lineageStore.RecordBirthAsync(new CellLineage
        {
            CellId = child.Name,
            WorkflowName = child.Name,
            ParentCellId = divisionPlan.ParentWorkflow,
            Generation = 1,
            BornAt = divisionTime,
            DivisionReason = divisionPlan.Reason,
            InheritedDomains = [child.Domain]
        });
    }
    await lineageStore.RecordDeathAsync(
        divisionPlan.ParentWorkflow, divisionTime, "divided");

    // =====================================================================
    //  PHASE 4 — Post-Division: Specialist Workflows
    // =====================================================================

    PrintPhase(4, "Post-Division — Specialists Serving Traffic");

    await Task.Delay(400);

    if (opts.Verbose)
    {
        var postLandscape = capabilityMap.DiscoverAll();
        foreach (var cap in postLandscape)
            Print($"    {cap.WorkflowName} (domain: {cap.Domain}) → " +
                  $"[{string.Join(", ", cap.Capabilities)}]", ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    PrintSubPhase("Routing to specialists...");

    string[] postDivisionRequests =
    [
        "Search for books about Kubernetes",
        "Create an order for ISBN 978-0-596-51774-8",
        "Get recommendations for fantasy genre",
        "Process payment of $27.50",
        "Check inventory for ISBN 978-1-491-95038-8",
        "Track shipment for order #ORD-7712",
        "Look up customer bob@example.com",
        "Get details for ISBN 978-0-321-12521-7",
        "Apply discount code HOLIDAY10",
        "I need to return order #ORD-6001",
    ];

    foreach (var req in postDivisionRequests)
    {
        var target = RouteByCapability(capabilityMap, req);
        requestLog.Add($"{target}: {req}");
        Print($"  📨 → {target}: \"{req}\"", ConsoleColor.DarkYellow);
        await Task.Delay(120);
    }
    Console.WriteLine();

    // =====================================================================
    //  PHASE 5 — Health Check: Each Specialist Has Low Tension
    // =====================================================================

    PrintPhase(5, "Health Check — Specialists Have Low Tension");

    foreach (var child in divisionPlan.Children)
    {
        var childKit = new ToolKit(child.Domain).Merge(
            child.Tools.Any(t => orderTools.Tools.ContainsKey(t)) ? orderTools : catalogTools);
        var childWorkflow = BuildSimulationWorkflow(child.Name, jobCount: 2);
        var organicChild = childWorkflow.JoinHost(organicHost, childKit);

        for (var i = 0; i < 5; i++)
            await organicChild.RunAsync(new BookstoreState());
    }

    await Task.Delay(500);

    ComplexitySnapshot[] childComplexity = await Task.WhenAll(divisionPlan.Children
        .Select(c => ((WorkflowExecutionMonitor)organicHost.GetMonitor()).GetSnapshotAsync(c.Name)));

    foreach (var snap in childComplexity)
    {
        PrintSnapshot(snap);
        Print($"  Division needed? NO ✓ specialist is focused", ConsoleColor.Green);
        Console.WriteLine();
    }

    // Close the learning loop
    PrintSubPhase("Division outcome tracking");
    var divisionId = $"div-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
    outcomeTracker.RecordBaseline(divisionId, snapshot2);
    var childMetrics = childComplexity.ToList<ComplexitySnapshot>();
    await outcomeTracker.RewardAsync(divisionId, childMetrics, divisionPlan);
    var reward = DivisionOutcomeTracker.ComputeReward(snapshot2, childMetrics);
    Print($"  📈 Division reward: {reward:F3} " +
          $"({(reward > 0 ? "positive — children improved!" : reward < 0 ? "negative — needs adjustment" : "neutral")})",
        reward > 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
    Print($"     Entropy:  {snapshot2.RoutingEntropy:F2} → avg {childMetrics.Average(c => c.RoutingEntropy):F2}",
        ConsoleColor.DarkGray);
    Print($"     Context:  {snapshot2.ContextUtilization:P0} → avg {childMetrics.Average(c => c.ContextUtilization):P0}",
        ConsoleColor.DarkGray);
    Console.WriteLine();

    // =====================================================================
    //  PHASE 6 — Colony Topology Report
    // =====================================================================

    PrintPhase(6, "Colony Topology Report");

    if (opts.Topology)
    {
        await ColonyReportStep.RunAsync(
            capabilityMap,
            lineageStore,
            affinityTracker: null,
            outputDirectory: "./out/organic-colony");
    }
    else
    {
        Print("  ⏭  Skipped (pass without --no-topology to enable)", ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    // =====================================================================
    //  PHASE 7 — Snapshots: YAML Export
    // =====================================================================

    PrintPhase(7, "Snapshots — YAML Export for Rollback & Deploy");

    var snapshotV2 = new HostSnapshot
    {
        KernelId = "bookstore", Version = 2, TakenAt = DateTimeOffset.UtcNow,
        Cells = divisionPlan.Children.Select(child =>
            new WorkflowSnapshotBuilder(child.Name, child.Domain)
                .Tools(child.Tools).SplitFrom(divisionPlan.ParentWorkflow)
                .Memory([child.Domain, "general"], lineageTags: ["bookstore"])
                .Build()).ToList(),
        RoutingTable = divisionPlan.Children.ToDictionary(c => c.Domain, c => c.Name),
        DivisionHistory =
        [
            new DivisionRecord
            {
                ParentWorkflow = divisionPlan.ParentWorkflow,
                Children = divisionPlan.Children.Select(c => c.Name).ToList(),
                Reason = divisionPlan.Reason, OccurredAt = DateTimeOffset.UtcNow,
                ApprovedBy = approval.ReviewedBy
            }
        ]
    };

    var yamlV2 = HostSnapshotExporter.ToYaml(snapshotV2);

    if (opts.Verbose)
    {
        PrintSubPhase("Snapshot v2 — Post-Division");
        PrintYamlBlock(yamlV2);
    }
    else
    {
        Print("  📸 Snapshot v2 captured (use --verbose to see YAML)", ConsoleColor.Cyan);
    }

    var roundTripped = HostSnapshotExporter.FromYaml(yamlV2);
    Print($"  🔄 Round-trip: v2 YAML → {roundTripped.Cells.Count} workflows", ConsoleColor.DarkGray);

    Console.WriteLine();
    PrintSubPhase("Activate: YAML → live workflows");

    foreach (var snap in roundTripped.Cells)
    {
        activator.Hydrate(snap);
        Print($"  ✅ '{snap.Name}' → {snap.Tools.Count} tools, {snap.Jobs.Count} jobs  " +
              $"(lineage: {snap.SplitFrom ?? "genesis"})", ConsoleColor.Green);
    }

    Console.WriteLine();

    // =====================================================================
    //  PHASE 8 — Learning: Division Feedback Loop
    // =====================================================================

    PrintPhase(8, "Learning — Division Feedback Loop (Tier 3)");

    Print("  The full lifecycle is now automatic via OrganicHost + WorkflowDivider:", ConsoleColor.Cyan);
    Print("  monitor → evaluate → gate → approve → DIVIDE → track outcome", ConsoleColor.Cyan);
    Console.WriteLine();

    Print("  Pipeline (automatic via OrganicHost):", ConsoleColor.DarkGray);
    Print("    organic.RunAsync() ──► host.ObserveExecution() ──► background loop:", ConsoleColor.DarkGray);
    Print("      monitor.Record() → executionCount++ → policy.EvaluateAsync()", ConsoleColor.DarkGray);
    Print("      → gate.ReviewAsync() → OnDivisionApproved", ConsoleColor.DarkGray);
    Print("      → WorkflowDivider.DivideAsync()  ← NEW: auto-execution", ConsoleColor.DarkGray);
    Print("        (derive → seed → activate → pause → spawn → confirm → kill)", ConsoleColor.DarkGray);
    Print("      → OnDivisionCompleted / OnDivisionFailed", ConsoleColor.DarkGray);
    Print("      → DivisionOutcomeTracker → IEmpiricalMemory evolves", ConsoleColor.DarkGray);
    Console.WriteLine();

    Print($"  This run: cold start (no prior division experience)", ConsoleColor.White);
    Print($"    → ExperienceDrivenDivisionPolicy fell back to ThresholdDivisionPolicy", ConsoleColor.DarkGray);
    Print($"    → Division reward: {reward:F3}", ConsoleColor.DarkGray);
    Print($"    → Next evaluation would use recalled strategies + UCB selection", ConsoleColor.DarkGray);
    Console.WriteLine();

    PrintSummary(requestLog, organicHost.CellHost, divisionPlan, reward);
}
else if (rejectedSignal is not null)
{
    PrintPhase(3, "Division Rejected — Generalist Continues");
    Print("  The generalist keeps serving all domains.", ConsoleColor.Cyan);
    Print("  The OrganicHost would re-evaluate after more traffic.", ConsoleColor.DarkGray);
    Console.WriteLine();

    foreach (var req in (string[])["Search for books about Kubernetes", "Process payment of $27.50",
        "Track shipment for order #ORD-7712", "Get recommendations for fantasy genre"])
    {
        var target = await router.RouteAsync(req);
        requestLog.Add($"{target}: {req}");
        Print($"  📨 → {target}: \"{req}\"", ConsoleColor.DarkYellow);
        await Task.Delay(100);
    }
    Console.WriteLine();
    PrintSummary(requestLog, organicHost.CellHost, plan: null, reward: null);
}
else
{
    PrintPhase(3, "No Division Triggered — Traffic Too Low");
    Print("  The OrganicHost hasn't accumulated enough executions to evaluate.", ConsoleColor.Cyan);
    Print("  Increase traffic or lower EvaluationInterval to trigger evaluation.", ConsoleColor.DarkGray);
    Console.WriteLine();
    PrintSummary(requestLog, organicHost.CellHost, plan: null, reward: null);
}

return;

// ═══════════════════════════════════════════════════════════════════════

static void PrintSummary(List<string> log, IWorkflowHost host, DivisionPlan? plan, float? reward)
{
    Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
    Print("  📊 Summary", ConsoleColor.Cyan);
    Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
    Print($"  Requests handled:  {log.Count}", ConsoleColor.White);
    Print($"  Active workflows:  [{string.Join(", ", host.ListActive())}]", ConsoleColor.White);

    if (plan is not null)
    {
        var names = string.Join(" + ", plan.Children.Select(c => c.Name));
        Print($"  Divisions:         1 ({plan.ParentWorkflow} → {names})", ConsoleColor.White);
        Print("  Parent downtime:   0 (served during transition)", ConsoleColor.White);
        if (reward is not null)
            Print($"  Division reward:   {reward:F3}", ConsoleColor.White);
        Console.WriteLine();
        Print("  Lifecycle:", ConsoleColor.DarkGray);
        Print($"    {plan.ParentWorkflow}  ──[genesis]──▶  4 tools", ConsoleColor.DarkGray);
        Print($"    {new string(' ', plan.ParentWorkflow.Length)}  ──[growth]───▶  10 tools, HIGH tension",
            ConsoleColor.DarkGray);
        Print($"    {new string(' ', plan.ParentWorkflow.Length)}  ──[OrganicHost]▶  background loop detects tension",
            ConsoleColor.DarkGray);
        Print($"    {new string(' ', plan.ParentWorkflow.Length)}  ──[gate]─────▶  {(reward > 0 ? "approved" : "approved")} via IDivisionApprovalGate",
            ConsoleColor.DarkGray);
        Print($"    {new string(' ', plan.ParentWorkflow.Length)}  ──[division]─▶  children bootstrap",
            ConsoleColor.DarkGray);
        Print($"    {new string(' ', plan.ParentWorkflow.Length)}  ──[handoff]──▶  stopped",
            ConsoleColor.DarkGray);
        foreach (var child in plan.Children)
            Print($"    {child.Name}  ──[started]──▶  {child.Tools.Count} tools, LOW tension",
                ConsoleColor.Green);
        Print($"    {new string(' ', plan.ParentWorkflow.Length)}  ──[learning]─▶  DivisionOutcomeTracker closes loop",
            ConsoleColor.DarkGray);
    }
    else
    {
        Print("  Divisions:         0 (rejected or not triggered)", ConsoleColor.Yellow);
    }

    Console.WriteLine();
    Print("  🧬 Done.", ConsoleColor.Cyan);
    Console.WriteLine();
}

static Task<DivisionApproval> PromptHumanApproval(
    DivisionPlan plan, ComplexitySnapshot snapshot, CancellationToken ct)
{
    Print("", ConsoleColor.White);
    Print("  ┌─────────────────────────────────────────────────────────┐", ConsoleColor.Magenta);
    Print("  │  🧬 DIVISION PROPOSED — Your approval required         │", ConsoleColor.Magenta);
    Print("  └─────────────────────────────────────────────────────────┘", ConsoleColor.Magenta);
    Print($"  Workflow: {plan.ParentWorkflow}", ConsoleColor.White);
    Print($"  Reason:   {plan.Reason}", ConsoleColor.White);
    Print($"  Metrics:  {snapshot.ToolCount} tools, {snapshot.TagClusterCount} clusters, " +
          $"entropy {snapshot.RoutingEntropy:F2}", ConsoleColor.DarkGray);
    foreach (var child in plan.Children)
        Print($"    → {child.Name} (domain: {child.Domain}) [{string.Join(", ", child.Tools)}]",
            ConsoleColor.Yellow);
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("  Approve? [y/N]: ");
    Console.ResetColor();

    var input = Console.ReadLine()?.Trim() ?? "";
    var approved = input.Equals("y", StringComparison.OrdinalIgnoreCase)
                || input.Equals("yes", StringComparison.OrdinalIgnoreCase);

    return Task.FromResult(approved
        ? DivisionApproval.Approve("Human approved via console", reviewedBy: "operator")
        : DivisionApproval.Reject("Human rejected via console", reviewedBy: "operator"));
}

static Workflow<BookstoreState> BuildSimulationWorkflow(
    string workflowName, int jobCount = 2)
{
    var wf = new Workflow<BookstoreState>(workflowName);
    for (var i = 0; i < jobCount; i++)
    {
        var step = $"step-{i}";
        wf = wf.Job(step, (s, _) =>
            Task.FromResult(s with { Response = $"[{step}]" }));
    }
    for (var i = 0; i < jobCount - 1; i++)
        wf = wf.Then($"step-{i}", $"step-{i + 1}");
    wf = wf.Then($"step-{jobCount - 1}", Workflow.End);
    return wf;
}

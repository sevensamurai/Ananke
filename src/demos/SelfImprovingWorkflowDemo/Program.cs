using Ananke.Design;
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using SelfImprovingWorkflowDemo;
using System.Text;
using System.Text.Json;

// -------------------------------------------------------------------
//  Ananke — Self-Improving Workflow Demo
//
//  Demonstrates an agent that diagnoses a missing tool in its own
//  workflow, recommends a fix, and the workflow is rebuilt with the
//  improvement applied.
//
//  No API keys required — uses simulated models.
//
//  Flow:
//    Run 1: expense-analyzer.ananke.yml (v1, no currency conversion)
//      → analyze agent cannot normalize EUR/GBP amounts
//      → review agent detects the gap using introspection tools
//      → review agent recommends adding a convert_currencies job
//
//    Run 2: expense-analyzer-v2.ananke.yml (v2, with currency conversion)
//      → convert_currencies code job normalizes all amounts to USD
//      → analyze agent receives clean data
//      → review agent confirms the workflow is now correct
// -------------------------------------------------------------------

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine("  Ananke — Self-Improving Workflow Demo");
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════
//  RUN 1 — Incomplete workflow (no currency conversion)
// ═══════════════════════════════════════════════════════════════════

Console.WriteLine("┌─────────────────────────────────────────────────────┐");
Console.WriteLine("│  RUN 1: expense-analyzer.ananke.yml (v1)            │");
Console.WriteLine("│  Expected: overseer detects missing currency tool   │");
Console.WriteLine("└─────────────────────────────────────────────────────┘");
Console.WriteLine();

var v1Result = await RunWorkflow("expense-analyzer.ananke.yml", includeConversion: false);

Console.WriteLine();
PrintDiagnosis(v1Result);

// ═══════════════════════════════════════════════════════════════════
//  RUN 2 — Fixed workflow (with currency conversion)
// ═══════════════════════════════════════════════════════════════════

Console.WriteLine();
Console.WriteLine("┌─────────────────────────────────────────────────────┐");
Console.WriteLine("│  RUN 2: expense-analyzer-v2.ananke.yml (v2)         │");
Console.WriteLine("│  Applied fix: added convert_currencies code job     │");
Console.WriteLine("└─────────────────────────────────────────────────────┘");
Console.WriteLine();

var v2Result = await RunWorkflow("expense-analyzer-v2.ananke.yml", includeConversion: true);

Console.WriteLine();
PrintDiagnosis(v2Result);

// ═══════════════════════════════════════════════════════════════════
//  Summary
// ═══════════════════════════════════════════════════════════════════

Console.WriteLine();
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine("  Summary");
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine($"  Run 1 passed:  {v1Result.ReviewPassed}");
Console.WriteLine($"  Run 2 passed:  {v2Result.ReviewPassed}");
Console.WriteLine();

if (!v1Result.ReviewPassed && v2Result.ReviewPassed)
{
    Console.WriteLine("  ✓ The workflow successfully self-diagnosed a missing capability,");
    Console.WriteLine("    recommended a fix, and the rebuilt workflow passed review.");
}

Console.WriteLine();
return;

// ===================================================================
//  Core: load manifest, build workflow, run, return results
// ===================================================================

async Task<DemoResult> RunWorkflow(string manifestFile, bool includeConversion)
{
    var yamlPath = Path.Combine(AppContext.BaseDirectory, manifestFile);
    var manifest = WorkflowManifest.Load(yamlPath);

    Console.WriteLine($"  Manifest:     {manifest.Name}");
    Console.WriteLine($"  Jobs:         {string.Join(", ", manifest.Jobs.Keys)}");
    Console.WriteLine($"  Connections:  {string.Join(" | ", manifest.Connections)}");
    Console.WriteLine();

    // -- Parse topology --
    var scaffold = WorkflowScaffold.Parse<ExpenseState>(manifest.Name, manifest.Connections);

    // -- Simulated expense data with foreign currencies --
    var rawExpenses = """
        1. Flight NYC→London: £1,240.00
        2. Hotel in Paris (3 nights): €890.50
        3. Client dinner in Tokyo: ¥42,000
        4. Taxi to airport: $45.00
        5. Conference registration: €350.00
        """;

    // -- Bind: extract (code job) --
    scaffold.Bind("extract", async (state, ct) =>
    {
        Console.WriteLine("  [extract] Loading raw expense data...");
        await Task.Delay(50, ct);
        return state with { RawExpenses = rawExpenses };
    });

    // -- Bind: convert_currencies (code job, only in v2) --
    if (includeConversion)
    {
        scaffold.Bind("convert_currencies", async (state, ct) =>
        {
            Console.WriteLine("  [convert_currencies] Normalizing all amounts to USD...");
            await Task.Delay(50, ct);

            // Simulated conversion using fixed rates
            var converted = state.RawExpenses!
                .Replace("£1,240.00", "$1,581.44 (£1,240.00 × 1.2754)")
                .Replace("€890.50", "$968.64 (€890.50 × 1.0878)")
                .Replace("¥42,000", "$280.00 (¥42,000 × 0.00667)")
                .Replace("€350.00", "$380.73 (€350.00 × 1.0878)");

            return state with
            {
                NormalizedExpenses = converted,
                CurrenciesConverted = true
            };
        });
    }

    // -- Bind: analyze (agent job) --
    // The simulated model returns different output depending on whether
    // it receives pre-converted data or raw foreign currencies.
    var analyzeModel = SimulatedModel.Dynamic(request =>
    {
        var prompt = request.Messages.LastOrDefault()?.Content ?? "";
        var hasConvertedData = prompt.Contains("× 1.2754") || prompt.Contains("× 1.0878");

        if (hasConvertedData)
        {
            return """
                {
                  "summary": "Total expenses: $3,255.81 USD. Breakdown: Flight $1,581.44, Hotel $968.64, Dinner $280.00, Taxi $45.00, Conference $380.73. All amounts verified in USD.",
                  "issues": []
                }
                """;
        }

        return """
            {
              "summary": "Total expenses: UNABLE TO COMPUTE — mixed currencies detected. Found GBP (£1,240.00), EUR (€890.50, €350.00), JPY (¥42,000), USD ($45.00). Cannot normalize without a currency conversion tool.",
              "issues": ["Foreign currency amounts (GBP, EUR, JPY) could not be converted to USD", "No convert_currency tool available to normalize amounts"]
            }
            """;
    });

    var analyzeJob = AgentJobFactory.Create<ExpenseState, AnalyzeResponse>("analyze", analyzeModel)
        .WithSystemPrompt(manifest.Jobs["analyze"].SystemPrompt!)
        .WithPrompt(state =>
        {
            var data = state.NormalizedExpenses ?? state.RawExpenses ?? "No data";
            return $"Analyze these expense line items and produce a USD-normalized summary:\n\n{data}";
        })
        .MapResult((state, response) =>
        {
            Console.WriteLine($"  [analyze] {(response.Issues?.Count > 0 ? $"⚠ {response.Issues.Count} issue(s)" : "✓ Analysis complete")}");
            return state with
            {
                AnalysisSummary = response.Summary ?? "",
                AnalysisIssues = response.Issues ?? []
            };
        })
        .Build();

    scaffold.Bind("analyze", analyzeJob);

    // -- Bind: review (overseer agent with introspection tools) --
    var introspectionTools = IntrospectionTools.Create(manifest);

    // The overseer model simulates calling introspection tools and reviewing results
    var reviewModel = SimulatedModel.Dynamic(request =>
    {
        var allContent = string.Join(" ", request.Messages.Select(m => m.Content ?? ""));
        var hasIssues = allContent.Contains("UNABLE TO COMPUTE") || allContent.Contains("could not be converted");

        if (hasIssues)
        {
            return """
                {
                  "passed": false,
                  "issues": [
                    "Analysis agent could not normalize foreign currencies (GBP, EUR, JPY) to USD",
                    "No currency conversion tool or code job is present in the workflow",
                    "Workflow topology goes directly from extract → analyze with no normalization step"
                  ],
                  "suggestions": [
                    "Add a 'convert_currencies' code job between 'extract' and 'analyze' that normalizes all amounts to USD",
                    "Alternative: add a 'convert_currency' tool to the analyze agent's ToolKit so it can convert on demand",
                    "Recommended manifest: expense-analyzer-v2.ananke.yml (adds convert_currencies code job)"
                  ],
                  "introspection": {
                    "used_tools": ["inspect_workflow", "search_docs", "suggest_fix"],
                    "inspect_result": "System prompts reference USD normalization but no conversion job/tool exists",
                    "docs_result": "For deterministic transformations like currency conversion, prefer code jobs over agent calls",
                    "fix_result": "Insert convert_currencies code job between extract and analyze"
                  }
                }
                """;
        }

        return """
            {
              "passed": true,
              "issues": [],
              "suggestions": [],
              "introspection": {
                "used_tools": ["inspect_workflow"],
                "inspect_result": "All jobs healthy, currencies pre-converted, analysis totals verified"
              }
            }
            """;
    });

    var reviewJob = AgentJobFactory.Create<ExpenseState, ReviewResponse>("review", reviewModel)
        .WithSystemPrompt(manifest.Jobs["review"].SystemPrompt!)
        .WithTools(introspectionTools)
        .WithPrompt(state => $"""
            Review the workflow output:

            Analysis summary: {state.AnalysisSummary}
            Analysis issues: {JsonSerializer.Serialize(state.AnalysisIssues)}
            Currencies pre-converted: {state.CurrenciesConverted}

            Use the introspection tools to inspect the workflow manifest and diagnose any problems.
            """)
        .WithMaxToolRounds(3)
        .MapResult((state, response) =>
        {
            Console.WriteLine($"  [review] {(response.Passed ? "✓ PASSED" : "✗ FAILED")}");
            if (response.Issues is { Count: > 0 })
            {
                foreach (var issue in response.Issues)
                    Console.WriteLine($"           ⚠ {issue}");
            }
            if (response.Suggestions is { Count: > 0 })
            {
                foreach (var suggestion in response.Suggestions)
                    Console.WriteLine($"           💡 {suggestion}");
            }
            return state with
            {
                ReviewPassed = response.Passed,
                ReviewIssues = response.Issues ?? [],
                ReviewSuggestions = response.Suggestions ?? []
            };
        })
        .Build();

    scaffold.Bind("review", reviewJob);

    // -- Build and run --
    var workflow = scaffold.Build();

    Console.WriteLine("  Running workflow...");
    Console.WriteLine();

    var result = await workflow.RunAsync(new ExpenseState());

    Console.WriteLine();
    Console.WriteLine($"  Status: {result.Status}");

    // -- Export Mermaid --
    Console.WriteLine();
    Console.WriteLine("  Topology:");
    Console.WriteLine(workflow.ToMermaid());

    return new DemoResult(
        ManifestName: manifest.Name,
        ReviewPassed: result.State.ReviewPassed,
        AnalysisSummary: result.State.AnalysisSummary,
        Issues: result.State.ReviewIssues,
        Suggestions: result.State.ReviewSuggestions);
}

static void PrintDiagnosis(DemoResult result)
{
    Console.WriteLine($"  ┌─ Diagnosis for '{result.ManifestName}' ─────────────");
    Console.WriteLine($"  │ Review passed: {result.ReviewPassed}");
    if (result.Issues.Count > 0)
    {
        Console.WriteLine($"  │ Issues:");
        foreach (var issue in result.Issues)
            Console.WriteLine($"  │   • {issue}");
    }
    if (result.Suggestions.Count > 0)
    {
        Console.WriteLine($"  │ Suggestions:");
        foreach (var s in result.Suggestions)
            Console.WriteLine($"  │   → {s}");
    }
    Console.WriteLine($"  └────────────────────────────────────────────────");
}

// ===================================================================
//  State & response records
// ===================================================================

record ExpenseState
{
    public string? RawExpenses { get; init; }
    public string? NormalizedExpenses { get; init; }
    public bool CurrenciesConverted { get; init; }
    public string AnalysisSummary { get; init; } = "";
    public IReadOnlyList<string> AnalysisIssues { get; init; } = [];
    public bool ReviewPassed { get; init; }
    public IReadOnlyList<string> ReviewIssues { get; init; } = [];
    public IReadOnlyList<string> ReviewSuggestions { get; init; } = [];
}

record AnalyzeResponse
{
    public string? Summary { get; init; }
    public List<string>? Issues { get; init; }
}

record ReviewResponse
{
    public bool Passed { get; init; }
    public List<string>? Issues { get; init; }
    public List<string>? Suggestions { get; init; }
}

record DemoResult(
    string ManifestName,
    bool ReviewPassed,
    string AnalysisSummary,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Suggestions);

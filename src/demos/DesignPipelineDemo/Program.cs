using Ananke.Design;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Configuration;
using System.Text;

// -- 1. Load secrets -------------------------------------------------
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("secrets.json", optional: true)    // local dev
    .AddEnvironmentVariables()                      // CI/CD (GitHub Actions secrets)
    .Build();

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("??? Ananke.Design — YAML + Agents ? Workflow ???");
Console.WriteLine();

// -- 2. Load workflow YAML -------------------------------------------
var yamlPath = Path.Combine(AppContext.BaseDirectory, "etl-pipeline.ananke.yml");
var manifest = WorkflowManifest.Load(yamlPath);

Console.WriteLine($"  Workflow: {manifest.Name}");
Console.WriteLine($"  Models:  {string.Join(", ", manifest.Models.Keys)}");
Console.WriteLine($"  Jobs:    {string.Join(", ", manifest.Jobs.Keys)}");
Console.WriteLine($"  Agent:   {string.Join(", ", manifest.Jobs.Where(j => j.Value.Type == "agent").Select(j => j.Key))}");
Console.WriteLine($"  Code:    {string.Join(", ", manifest.Jobs.Where(j => j.Value.Type == "code").Select(j => j.Key))}");
Console.WriteLine();

// -- 3. Resolve models from secrets ----------------------------------
var models = new ModelResolver()
    .Register("openai", "OpenAI", OpenAIChatAgentModel.Create)
    .Register("anthropic", "Anthropic", AnthropicAgentModel.Create)
    .Resolve(manifest, key => config[key]);
foreach (var (alias, _) in models)
    Console.WriteLine($"  ? Model '{alias}' resolved");
Console.WriteLine();

// -- 4. Parse topology and bind jobs ---------------------------------
var scaffold = WorkflowScaffold.Parse<PipelineState>(manifest.Name, manifest.Connections);

Console.WriteLine($"  Discovered: {string.Join(", ", scaffold.JobNames)}");
Console.WriteLine($"  Unbound:    {string.Join(", ", scaffold.UnboundJobs)}");
Console.WriteLine();

// -- Tools (code-only — implementations can't live in YAML) ----------
var dataTools = new ToolKit("data-tools")
    .AddTool(
        "list_datasets",
        "Lists available dataset names in the data warehouse",
        () => "revenue_q4_2024, csat_survey_2024, churn_monthly, nps_scores")
    .AddTool(
        "describe_dataset",
        "Returns schema and row count for a dataset",
        (name) => name switch
        {
            "revenue_q4_2024"  => "columns: date, region, amount | rows: 12,400",
            "csat_survey_2024" => "columns: date, score, channel | rows: 8,200",
            "churn_monthly"    => "columns: month, rate, segment | rows: 36",
            "nps_scores"       => "columns: date, score, cohort  | rows: 4,100",
            _ => $"Unknown dataset: {name}"
        },
        "name", "The dataset name to describe");

// Bind agent jobs from YAML config
foreach (var (jobName, jobDef) in manifest.Jobs.Where(j => j.Value.Type == "agent"))
{
    var model = models[jobDef.ModelAlias!];
    var builder = AgentJobFactory.Create<PipelineState, AgentTextResponse>(jobName, model)
        .WithSystemPrompt(jobDef.SystemPrompt!)
        .WithPrompt(state => BuildPromptForJob(jobName, state))
        .MapResult((state, response) => ApplyAgentResult(jobName, state, response.Text ?? ""))
        .WithMaxToolRounds(jobDef.MaxToolRounds);

    // Attach tools to specific jobs — tools are code, not YAML
    if (jobName == "plan")
        builder.WithTools(dataTools);

    scaffold.Bind(jobName, builder.Build());
    Console.WriteLine($"  ? Bound agent job '{jobName}' ? {jobDef.ModelAlias}");
}

// Bind code jobs
scaffold
    .Bind("fetch_a", async (state, ct) =>
    {
        Console.WriteLine("  [fetch_a] Fetching dataset A...");
        await Task.Delay(100, ct);
        return state with { RawA = $"Dataset A results for: {state.PlanA ?? "general"}" };
    })
    .Bind("fetch_b", async (state, ct) =>
    {
        Console.WriteLine("  [fetch_b] Fetching dataset B...");
        await Task.Delay(100, ct);
        return state with { RawB = $"Dataset B results for: {state.PlanB ?? "general"}" };
    });

// Bind merge
scaffold.BindMerge("combine", branches =>
{
    var a = branches.FirstOrDefault(b => b.TransformedA is not null);
    var b = branches.FirstOrDefault(b => b.TransformedB is not null);
    return new PipelineState
    {
        Step = "joined",
        TransformedA = a?.TransformedA ?? "",
        TransformedB = b?.TransformedB ?? ""
    };
});

Console.WriteLine();

// -- 5. Build and run ------------------------------------------------
var workflow = scaffold.Build();

Console.WriteLine("  Running workflow...");
Console.WriteLine();

var result = await workflow.RunAsync(new PipelineState
{
    Step = "start",
    PlanA = "revenue trends Q4 2024",
    PlanB = "customer satisfaction metrics"
});

Console.WriteLine();
Console.WriteLine($"  Status: {result.Status}");
Console.WriteLine($"  Output: {result.State.Output?[..Math.Min(200, result.State.Output?.Length ?? 0)]}...");
Console.WriteLine();

// -- 6. Mermaid ------------------------------------------------------
Console.WriteLine(workflow.ToMermaid());

return;

// -------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------

static string BuildPromptForJob(string jobName, PipelineState state) => jobName switch
{
    "plan" => $"""
        Plan an ETL pipeline for these two data requests:
        A: {state.PlanA ?? "general data"}
        B: {state.PlanB ?? "general data"}
        Return JSON with fields "fetch_a_query" and "fetch_b_query".
        """,
    "transform_a" => $"Transform this raw data into a clean summary:\n\n{state.RawA}",
    "transform_b" => $"Transform this raw data into a clean summary:\n\n{state.RawB}",
    "combine" => $"""
        Combine these two dataset summaries into a final report:

        Dataset A:
        {state.TransformedA}

        Dataset B:
        {state.TransformedB}
        """,
    _ => $"Process: {state.Step}"
};

static PipelineState ApplyAgentResult(string jobName, PipelineState state, string text) => jobName switch
{
    "plan" => state with { Step = "planned", PlanA = text, PlanB = text },
    "transform_a" => state with { TransformedA = text },
    "transform_b" => state with { TransformedB = text },
    "combine" => state with { Output = text },
    _ => state
};

// -------------------------------------------------------------------
// State
// -------------------------------------------------------------------

record PipelineState
{
    public string Step { get; init; } = "";
    public string? PlanA { get; init; }
    public string? PlanB { get; init; }
    public string? RawA { get; init; }
    public string? RawB { get; init; }
    public string? TransformedA { get; init; }
    public string? TransformedB { get; init; }
    public string? Output { get; init; }
}

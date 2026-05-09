using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using System.ClientModel;

// ---------------------------------------------------------------------
//  BasicAgentDemo — progressive introduction to Ananke:
//
//    Level 0  (default)  Pure workflow, no LLM, no API key — runs instantly
//    Level 1  (--level 1)  Direct model call
//    Level 2  (--level 2)  Capability-based routing + ModelCatalog
//    Level 3  (--level 3)  Full workflow with AgentJob + tools + router
//    Level 4  (--level 4)  Streaming chat workflow + tools + OpenTelemetry tracing
//
//  Usage:
//    dotnet run                 # Level 0 only (always works)
//    dotnet run -- --level 2    # Levels 0–2 (needs OpenAI key)
//    dotnet run -- --level all  # All levels
// ---------------------------------------------------------------------

var maxLevel = ParseLevel(args);

Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine("  Ananke — BasicAgentDemo");
Console.WriteLine("-----------------------------------------------------------");

// =====================================================================
//  LEVEL 0 — Pure workflow (no LLM, no API key, no network)
// =====================================================================
Console.WriteLine();
Console.WriteLine("-- Level 0: Pure workflow (no LLM required) -------------");

// The simplest possible workflow — 5 lines, string state.
var hello = new Workflow<string>("hello")
    .Job("greet", (s, _) => Task.FromResult($"Hello, {s}!"))
    .Then("greet", Workflow.End);

var helloExec = await hello.RunAsync("World");
Console.WriteLine($"  {helloExec.State}");

// A multi-step data pipeline showing Chain, state records, and transforms.
var pipeline = new Workflow<PipelineState>("data-pipeline")
    .Job("fetch", (s, _) => Task.FromResult(s with
    {
        RawData = ["Tokyo:14M", "London:9M", "NYC:8M", "Paris:2M"]
    }))
    .Job("transform", (s, _) => Task.FromResult(s with
    {
        Cities = s.RawData
            .Select(r => r.Split(':'))
            .ToDictionary(p => p[0], p => p[1])
    }))
    .Job("summarize", (s, _) => Task.FromResult(s with
    {
        Summary = $"Processed {s.Cities.Count} cities: {string.Join(", ", s.Cities.Keys)}"
    }))
    .Chain("fetch", "transform", "summarize")
    .Then("summarize", Workflow.End);

var pipeExec = await pipeline.RunAsync(new PipelineState());
Console.WriteLine($"  Pipeline [{pipeExec.Status}]");
Console.WriteLine($"    Cities:  {string.Join(", ", pipeExec.State.Cities.Select(kv => $"{kv.Key}={kv.Value}"))}");
Console.WriteLine($"    Summary: {pipeExec.State.Summary}");

if (maxLevel < 1)
{
    PrintNextSteps();
    return;
}

// --- Levels 1–3 require an OpenAI API key ---------------------------

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("secrets.json", optional: true)
    .Build();

var apiKey = config["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException(
        "Levels 1–3 require an OpenAI API key. " +
        "Add { \"OpenAI\": { \"ApiKey\": \"sk-...\" } } to secrets.json");

IStreamingAgentModel miniModel = new OpenAIChatAgentModel(
    new ChatClient("gpt-4.1-mini", new ApiKeyCredential(apiKey)));

IStreamingAgentModel fullModel = new OpenAIChatAgentModel(
    new ChatClient("gpt-4.1", new ApiKeyCredential(apiKey)));

// =====================================================================
//  LEVEL 1 — Direct model call (simplest LLM usage)
// =====================================================================
Console.WriteLine();
Console.WriteLine("-- Level 1: Direct model call (gpt-4.1-mini) -----------");

var directResponse = await miniModel.GenerateAsync(new AgentRequest
{
    SystemPrompt = "You are a concise assistant. Answer in one or two sentences.",
    Messages = [AgentMessage.User("What is the capital of Japan?")]
});
Console.WriteLine($"  Response: {directResponse.Text}");

if (maxLevel < 2) return;

// =====================================================================
//  LEVEL 2 — Capability-based routing with ModelCatalog
// =====================================================================
Console.WriteLine();
Console.WriteLine("-- Level 2: Capability-based routing --------------------");

// ModelCatalog provides stable metadata (capabilities, context, speed/intelligence).
// You supply cost rates from your provider's pricing page.
var miniRates = new ModelCostRates(CostPer1KInputTokens: 0.0004m, CostPer1KOutputTokens: 0.0016m);
var fullRates = new ModelCostRates(CostPer1KInputTokens: 0.002m, CostPer1KOutputTokens: 0.008m);

var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
    .AddModel(ModelCatalog.OpenAI.Gpt4_1Mini.ToProfile(miniModel, miniRates))
    .AddModel(ModelCatalog.OpenAI.Gpt4_1.ToProfile(fullModel, fullRates));

// 2a — Simple text request ? routes to gpt-4.1-mini (cheaper, meets requirements)
var simpleRequest = new AgentRequest
{
    SystemPrompt = "You are a concise assistant.",
    Messages = [AgentMessage.User("Explain what a hash table is in one sentence.")]
};
var simpleModel = router.Select(simpleRequest);
var simpleResponse = await simpleModel.GenerateAsync(simpleRequest);
Console.WriteLine($"  [Simple]    ? {ModelName(simpleModel)} ? {simpleResponse.Text}");

// 2b — Reasoning request ? metadata bumps requirements, routes to gpt-4.1
var reasoningRequest = new AgentRequest
{
    SystemPrompt = "You are a senior software architect.",
    Messages = [AgentMessage.User("Compare event sourcing vs CRUD for a banking ledger.")]
}
.WithRequiredCapabilities(ModelCapability.Reasoning)
.WithMinIntelligence(3);

var reasoningModel = router.Select(reasoningRequest);
var reasoningResponse = await reasoningModel.GenerateAsync(reasoningRequest);
Console.WriteLine($"  [Reasoning] ? {ModelName(reasoningModel)}");
Console.WriteLine($"               {Truncate(reasoningResponse.Text, 200)}");

// 2c — Code generation ? inferred from capability flag
var codeRequest = new AgentRequest
{
    SystemPrompt = "You are a C# expert. Respond only with code.",
    Messages = [AgentMessage.User("Write a C# method that checks if a string is a palindrome.")]
}
.WithRequiredCapabilities(ModelCapability.CodeGeneration);

var codeModel = router.Select(codeRequest);
var codeResponse = await codeModel.GenerateAsync(codeRequest);
Console.WriteLine($"  [Code gen]  ? {ModelName(codeModel)}");
Console.WriteLine($"               {Truncate(codeResponse.Text, 200)}");

if (maxLevel < 3) return;

// =====================================================================
//  LEVEL 3 — Full workflow with AgentJob + tools + routed model
// =====================================================================
Console.WriteLine();
Console.WriteLine("-- Level 3: Workflow + AgentJob + tools + routing -------");

var researchTools = new ToolKit("research")
    .AddTool(
        "lookup_population",
        "Looks up the population of a country.",
        (string country) => country.ToUpperInvariant() switch
        {
            "JAPAN" => "125.7 million (2024)",
            "BRAZIL" => "216.4 million (2024)",
            "GERMANY" => "84.5 million (2024)",
            _ => $"Population data not available for {country}"
        },
        "country", "The country name to look up");

// Step 1 — gather: uses tools (mini is fine for tool calling)
var gatherJob = new AgentJob<ResearchState, GatherResult>
    .Builder("gather", router)
    .WithSystemPrompt("You are a research assistant. Use tools to gather data, then return a JSON summary.")
    .WithPrompt(s => $"Look up the population of {s.Country} and summarise the result.")
    .WithTools(researchTools)
    .MapResult((s, r) => s with { Facts = r.Summary })
    .Build();

// Step 2 — analyze: requires reasoning (full model will be selected)
var analyzeJob = new AgentJob<ResearchState, AnalysisResult>
    .Builder("analyze", router)
    .WithSystemPrompt("""
        You are a senior analyst. You will receive research facts.
        Provide a brief analytical insight. Respond as JSON.
        """)
    .WithPrompt(s => $"Country: {s.Country}\nFacts: {s.Facts}\n\nProvide an analytical insight about this country's demographics.")
    .MapResult((s, r) => s with { Analysis = r.Insight })
    .Build();

var research = new Workflow<ResearchState>("country-research")
    .Job("gather", gatherJob)
    .Job("analyze", analyzeJob)
    .Chain("gather", "analyze")
    .Then("analyze", Workflow.End);

var researchExec = await research.RunAsync(new ResearchState { Country = "Japan" });

Console.WriteLine($"  Status:   {researchExec.Status}");
Console.WriteLine($"  Country:  {researchExec.State.Country}");
Console.WriteLine($"  Facts:    {researchExec.State.Facts}");
Console.WriteLine($"  Analysis: {researchExec.State.Analysis}");

if (maxLevel < 4)
{
    Console.WriteLine();
    Console.WriteLine("-----------------------------------------------------------");
    Console.WriteLine("  Done.");
    Console.WriteLine("-----------------------------------------------------------");
    PrintNextSteps();
    return;
}

// =====================================================================
//  LEVEL 4 — Streaming chat workflow + stock tools + OpenTelemetry
//            (absorbed from SimpleWorkflowDemo)
// =====================================================================
Console.WriteLine();
Console.WriteLine("-- Level 4: Streaming chat workflow + OTel tracing ------");

await BasicAgentDemo.Workflow.Level4.RunAsync(config);

// -- Helpers ----------------------------------------------------------

static int ParseLevel(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (!args[i].Equals("--level", StringComparison.OrdinalIgnoreCase))
            continue;

        var value = args[i + 1];
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
            return int.MaxValue;
        if (int.TryParse(value, out var level))
            return level;
    }

    return 0;
}

static void PrintNextSteps()
{
    Console.WriteLine();
    Console.WriteLine("  --- Next steps ---");
    Console.WriteLine("  Add an OpenAI key to secrets.json and run higher levels:");
    Console.WriteLine("    dotnet run -- --level 1   Direct model call");
    Console.WriteLine("    dotnet run -- --level 2   Capability-based routing");
    Console.WriteLine("    dotnet run -- --level 3   Full workflow + AgentJob + tools");
    Console.WriteLine("    dotnet run -- --level 4   Streaming chat + tools + OTel tracing");
    Console.WriteLine("    dotnet run -- --level all  All levels");
}

static string ModelName(IAgentModel model) => model switch
{
    RoutedAgentModel => "routed",
    OpenAIChatAgentModel oai => oai.ToString() ?? "OpenAI",
    _ => model.GetType().Name
};

static string Truncate(string? text, int maxLength) =>
    text is null ? "(empty)"
    : text.Length <= maxLength ? text.ReplaceLineEndings(" ")
    : string.Concat(text.AsSpan(0, maxLength).ToString().ReplaceLineEndings(" "), "…");

// -- State records ----------------------------------------------------

public record PipelineState
{
    public IReadOnlyList<string> RawData { get; init; } = [];
    public IReadOnlyDictionary<string, string> Cities { get; init; } = new Dictionary<string, string>();
    public string Summary { get; init; } = "";
}

public record ResearchState
{
    public string Country { get; init; } = "";
    public string? Facts { get; init; }
    public string? Analysis { get; init; }
}

public record GatherResult
{
    public string Summary { get; init; } = "";
}

public record AnalysisResult
{
    public string Insight { get; init; } = "";
}

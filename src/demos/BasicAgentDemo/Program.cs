using System.ClientModel;
using Ananke.Orchestration;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

// ---------------------------------------------------------------------
//  BasicAgentDemo — demonstrates three levels of model usage:
//    1. Direct model call (simplest)
//    2. Capability-based routing (automatic cost-effective selection)
//    3. Full workflow with AgentJob + routed model
// ---------------------------------------------------------------------

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("secrets.json", optional: true)
    .Build();

var apiKey = config["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey missing from secrets.json");

// -- Create two models at different tiers ----------------------------
//    In production these could be entirely different providers.

IStreamingAgentModel miniModel = new OpenAIChatAgentModel(
    new ChatClient("gpt-4.1-mini", new ApiKeyCredential(apiKey)));

IStreamingAgentModel fullModel = new OpenAIChatAgentModel(
    new ChatClient("gpt-4.1", new ApiKeyCredential(apiKey)));

Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine("  Ananke — BasicAgentDemo");
Console.WriteLine("-----------------------------------------------------------");

// =====================================================================
//  PART 1 — Direct model call (specify the model explicitly)
// =====================================================================
Console.WriteLine();
Console.WriteLine("-- Part 1: Direct model call (gpt-4.1-mini) --");
Console.WriteLine();

var directRequest = new AgentRequest
{
    SystemPrompt = "You are a concise assistant. Answer in one or two sentences.",
    Messages = [AgentMessage.User("What is the capital of Japan?")]
};

var directResponse = await miniModel.GenerateAsync(directRequest);
Console.WriteLine($"  Response: {directResponse.Text}");

// =====================================================================
//  PART 2 — CapabilityModelRouter (automatic selection)
// =====================================================================
Console.WriteLine();
Console.WriteLine("-- Part 2: Capability-based routing --");
Console.WriteLine();

// Define model profiles with capabilities, cost, and intelligence tiers.
var miniProfile = new ModelProfile
{
    Name = "gpt-4.1-mini",
    Model = miniModel,
    Capabilities = ModelCapability.TextGeneration | ModelCapability.ToolCalling
                 | ModelCapability.StructuredOutput | ModelCapability.LargeContext,
    IntelligenceTier = 2,
    CostPer1KTokens = 0.40m,
    MaxContextTokens = 1_047_576,
    SpeedTier = 4
};

var fullProfile = new ModelProfile
{
    Name = "gpt-4.1",
    Model = fullModel,
    Capabilities = ModelCapability.TextGeneration | ModelCapability.CodeGeneration
                 | ModelCapability.Reasoning | ModelCapability.ToolCalling
                 | ModelCapability.StructuredOutput | ModelCapability.LargeContext,
    IntelligenceTier = 4,
    CostPer1KTokens = 2.00m,
    MaxContextTokens = 1_047_576,
    SpeedTier = 3
};

var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
    .AddModel(miniProfile)
    .AddModel(fullProfile);

// 2a — Simple text request ? should route to gpt-4.1-mini (cheaper, meets requirements)
var simpleRequest = new AgentRequest
{
    SystemPrompt = "You are a concise assistant.",
    Messages = [AgentMessage.User("Explain what a hash table is in one sentence.")]
};

var simpleModel = router.Select(simpleRequest);
var simpleResponse = await simpleModel.GenerateAsync(simpleRequest);
Console.WriteLine($"  [Simple text]   ? {ModelNameOf(simpleModel)} ? {simpleResponse.Text}");

// 2b — Reasoning request ? metadata bumps min_intelligence, routes to gpt-4.1
var reasoningRequest = new AgentRequest
{
    SystemPrompt = "You are a senior software architect.",
    Messages = [AgentMessage.User("Compare the trade-offs between event sourcing and CRUD for a banking ledger.")]
}
.WithRequiredCapabilities(ModelCapability.Reasoning)
.WithMinIntelligence(3);

var reasoningModel = router.Select(reasoningRequest);
var reasoningResponse = await reasoningModel.GenerateAsync(reasoningRequest);
Console.WriteLine($"  [Reasoning]     ? {ModelNameOf(reasoningModel)}");
Console.WriteLine($"                    {Truncate(reasoningResponse.Text, 200)}");

// 2c — Code generation request ? inferred from capability flag
var codeRequest = new AgentRequest
{
    SystemPrompt = "You are a C# expert. Respond only with code.",
    Messages = [AgentMessage.User("Write a C# method that checks if a string is a palindrome.")]
}
.WithRequiredCapabilities(ModelCapability.CodeGeneration);

var codeModel = router.Select(codeRequest);
var codeResponse = await codeModel.GenerateAsync(codeRequest);
Console.WriteLine($"  [Code gen]      ? {ModelNameOf(codeModel)}");
Console.WriteLine($"                    {Truncate(codeResponse.Text, 200)}");

// =====================================================================
//  PART 3 — Workflow with AgentJob + CapabilityModelRouter
// =====================================================================
Console.WriteLine();
Console.WriteLine("-- Part 3: Workflow with AgentJob + routed model --");
Console.WriteLine();

// A simple state for our two-step workflow
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

// Step 1 — gather: uses tools (mini is fine — tool calling doesn't need frontier)
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

var workflow = new Workflow<ResearchState>("country-research")
    .Job("gather", gatherJob)
    .Job("analyze", analyzeJob)
    .Then("gather", "analyze")
    .Then("analyze", Workflow.End);

var execution = await workflow.RunAsync(new ResearchState { Country = "Japan" });

Console.WriteLine($"  Status:   {execution.Status}");
Console.WriteLine($"  Country:  {execution.State.Country}");
Console.WriteLine($"  Facts:    {execution.State.Facts}");
Console.WriteLine($"  Analysis: {execution.State.Analysis}");

Console.WriteLine();
Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine("  Done.");
Console.WriteLine("-----------------------------------------------------------");

// -- Helpers ----------------------------------------------------------

static string ModelNameOf(IAgentModel model) => model switch
{
    RoutedAgentModel => "routed (resolved at call time)",
    OpenAIChatAgentModel => "OpenAI (direct)",
    _ => model.GetType().Name
};

static string Truncate(string? text, int maxLength) =>
    text is null ? "(empty)"
    : text.Length <= maxLength ? text.ReplaceLineEndings(" ")
    : string.Concat(text.AsSpan(0, maxLength).ToString().ReplaceLineEndings(" "), "…");

// -- State & response records ----------------------------------------

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

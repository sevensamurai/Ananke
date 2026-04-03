using Ananke.Orchestration;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

// ─────────────────────────────────────────────────────────────────────
//  McpServerDemo — Ananke tools and workflows as an MCP server
//
//  This runs as a LOCAL process on your machine. MCP clients (VS Code,
//  Claude Desktop, etc.) launch it and communicate over stdin/stdout.
//  Nothing is exposed to the network — no ports, no HTTP, no cloud.
//
//  See README.md for setup instructions.
// ─────────────────────────────────────────────────────────────────────

// ── 1. Define tools ──────────────────────────────────────────────────

var mathTools = new ToolKit("math")
    .AddTool("add", "Adds two numbers", b => b
        .Param<double>("a", "First number")
        .Param<double>("b", "Second number")
        .OnExecute(args => ToolResult.Ok($"{args.Get<double>("a") + args.Get<double>("b")}")))
    .AddTool("multiply", "Multiplies two numbers", b => b
        .Param<double>("a", "First number")
        .Param<double>("b", "Second number")
        .OnExecute(args => ToolResult.Ok($"{args.Get<double>("a") * args.Get<double>("b")}")));

var textTools = new ToolKit("text")
    .AddTool(
        "word_count", "Counts words in the given text",
        (string text) => $"{text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} words",
        "text", "The text to count words in")
    .AddTool(
        "reverse", "Reverses a string",
        (string text) => new string(text.Reverse().ToArray()),
        "text", "The text to reverse")
    .AddTool(
        "uppercase", "Converts text to uppercase",
        (string text) => text.ToUpperInvariant(),
        "text", "The text to convert");

var lookupTools = new ToolKit("lookup")
    .AddTool(
        "country_population", "Returns the population of a country",
        (string country) => country.Trim().ToUpperInvariant() switch
        {
            "JAPAN" => "125.7 million (2024 est.)",
            "BRAZIL" => "216.4 million (2024 est.)",
            "GERMANY" => "84.5 million (2024 est.)",
            "INDIA" => "1.44 billion (2024 est.)",
            "UNITED STATES" or "USA" or "US" => "335 million (2024 est.)",
            "NIGERIA" => "230 million (2024 est.)",
            _ => $"No data available for '{country}'"
        },
        "country", "The country name to look up")
    .AddTool(
        "country_capital", "Returns the capital city of a country",
        (string country) => country.Trim().ToUpperInvariant() switch
        {
            "JAPAN" => "Tokyo",
            "BRAZIL" => "Brasília",
            "GERMANY" => "Berlin",
            "INDIA" => "New Delhi",
            "UNITED STATES" or "USA" or "US" => "Washington, D.C.",
            "NIGERIA" => "Abuja",
            _ => $"No data available for '{country}'"
        },
        "country", "The country name to look up");

// ── 2. Define a workflow ─────────────────────────────────────────────
//
//  A simple data pipeline: validate → enrich → format → __end__
//  No LLM needed — pure delegate jobs. When the MCP client calls
//  "run_data_pipeline", all three steps execute and the final state
//  is returned as JSON.

var dataPipeline = new Workflow<PipelineState>("data-pipeline")
    .Job("validate", (state, _) => Task.FromResult(state with
    {
        IsValid = !string.IsNullOrWhiteSpace(state.Input),
        Status = string.IsNullOrWhiteSpace(state.Input) ? "INVALID" : "VALIDATED"
    }))
    .Job("enrich", (state, _) => Task.FromResult(state with
    {
        WordCount = state.Input?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0,
        CharCount = state.Input?.Length ?? 0,
        Status = state.IsValid ? "ENRICHED" : state.Status
    }))
    .Job("format", (state, _) => Task.FromResult(state with
    {
        Output = state.IsValid
            ? $"[{state.WordCount} words, {state.CharCount} chars] {state.Input!.ToUpperInvariant()}"
            : "ERROR: Invalid input",
        Status = state.IsValid ? "COMPLETE" : "FAILED"
    }))
    .Chain("validate", "enrich", "format")
    .Then("format", Workflow.End);

// ── 3. Build the MCP stdio server ───────────────────────────────────
//
//  CreateEmptyApplicationBuilder avoids the default console logging
//  that would corrupt the JSON-RPC messages on stdout.

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "ananke-demo",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithAnankeTools(mathTools, textTools, lookupTools)
    .WithAnankeWorkflow<PipelineState>(
        name: "run_data_pipeline",
        description: "Runs a 3-step data pipeline (validate → enrich → format). " +
                     "Returns the processed result as JSON.",
        workflow: dataPipeline,
        stateFactory: args =>
        {
            var input = args.TryGetValue("input", out var el) ? el.GetString() ?? "" : "";
            return new PipelineState { Input = input };
        });

await builder.Build().RunAsync();

// ── State record ─────────────────────────────────────────────────────

public record PipelineState
{
    public string? Input { get; init; }
    public bool IsValid { get; init; }
    public int WordCount { get; init; }
    public int CharCount { get; init; }
    public string? Output { get; init; }
    public string Status { get; init; } = "PENDING";
}

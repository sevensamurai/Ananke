using A2A;
using Ananke.A2A.Server;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Tools;

namespace AgentToAgentProtocolDemo;

/// <summary>
/// A simple A2A agent that exposes two Ananke capabilities:
///   1. A ToolKit with text utilities (word count, reverse, uppercase)
///   2. A Workflow that runs a 3-step data pipeline (validate → enrich → format)
///
/// The agent is wired into a <see cref="TaskManager"/> via <see cref="WorkflowTaskAdapter"/>
/// and served over HTTP as a standard A2A JSON-RPC endpoint.
/// </summary>
internal static class EchoAgent
{
    // ── Tools ────────────────────────────────────────────────────────

    internal static ToolKit CreateTools() => new ToolKit("text")
        .AddTool(
            "word_count", "Counts the number of words in the given text.",
            (string text) => $"{text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} words",
            "text", "The text to count words in")
        .AddTool(
            "reverse", "Reverses a string.",
            (string text) => new string(text.Reverse().ToArray()),
            "text", "The text to reverse")
        .AddTool(
            "uppercase", "Converts text to UPPERCASE.",
            (string text) => text.ToUpperInvariant(),
            "text", "The text to convert");

    // ── Workflow ─────────────────────────────────────────────────────

    internal static Workflow<PipelineState> CreatePipeline() =>
        new Workflow<PipelineState>("text-pipeline")
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

    // ── Agent Card ───────────────────────────────────────────────────

    internal static AgentCard BuildCard(string agentUrl, ToolKit tools) =>
        new AgentCardBuilder()
            .WithName("Ananke Echo Agent")
            .WithDescription(
                "A demo A2A agent that processes text. Send any text and it will " +
                "be run through a 3-step pipeline (validate → enrich → format).")
            .WithVersion("1.0.0")
            .WithSkillsFrom(tools)
            .WithSkill(new AgentSkill
            {
                Id = "text_pipeline",
                Name = "Text Pipeline",
                Description = "Validates, enriches, and formats text input",
                Tags = ["pipeline", "text"]
            })
            .Build(agentUrl);

    // ── Wire into TaskManager ────────────────────────────────────────

    internal static void Attach(TaskManager taskManager, string agentUrl)
    {
        var tools = CreateTools();
        var pipeline = CreatePipeline();
        var card = BuildCard(agentUrl, tools);

        var adapter = new WorkflowTaskAdapter(async (input, ct) =>
        {
            // Try tools first — if input starts with a known command
            var (command, arg) = ParseCommand(input);
            if (command is not null && tools.Tools.TryGetValue(command, out var tool))
            {
                var result = await tool.Execute(
                    new Dictionary<string, object?> { ["text"] = arg }, ct);
                return result.Value;
            }

            // Otherwise run the full pipeline
            var execution = await pipeline.RunAsync(
                new PipelineState { Input = input }, ct);
            return execution.State.Output ?? execution.State.Status;
        });

        adapter.Attach(taskManager, card);
    }

    private static (string? Command, string Arg) ParseCommand(string input)
    {
        // Simple "command: argument" parsing for tool dispatch
        var colonIndex = input.IndexOf(':');
        if (colonIndex <= 0)
            return (null, input);

        var command = input[..colonIndex].Trim().ToLowerInvariant().Replace(' ', '_');
        var arg = input[(colonIndex + 1)..].Trim();
        return (command, arg);
    }
}

// ── Pipeline state ───────────────────────────────────────────────────

internal record PipelineState
{
    public string? Input { get; init; }
    public bool IsValid { get; init; }
    public int WordCount { get; init; }
    public int CharCount { get; init; }
    public string? Output { get; init; }
    public string Status { get; init; } = "PENDING";
}

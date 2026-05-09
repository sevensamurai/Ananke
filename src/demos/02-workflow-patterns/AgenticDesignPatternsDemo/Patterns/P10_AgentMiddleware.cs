using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Middleware;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P10_AgentMiddleware
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("10. Agent-Level Middleware (guardrails + logging)");

        var innerModel = SimulatedModel.Fixed("""{"Summary":"The data shows a 15% increase in Q3."}""");

        var guardrail = new GuardrailAgentModelMiddleware.Builder()
            .DenyPattern("pii-ssn", @"\b\d{3}-\d{2}-\d{4}\b")
            .DenyWhen("empty-response", (resp, _) => string.IsNullOrWhiteSpace(resp.Text))
            .Build();

        var safeModel = MiddlewareAgentModel.Wrap(innerModel, guardrail);

        var agent = AgentJobFactory.Create<MiddlewareState, SummaryResponse>("summarize", safeModel)
            .WithSystemPrompt("Summarize the provided data. Never include personal information.")
            .WithPrompt(s => s.Data)
            .MapResult((s, r) => s with { Summary = r.Summary ?? "" })
            .Build();

        var workflow = new Workflow<MiddlewareState>("guarded-workflow")
            .Job("summarize", agent)
            .Then("summarize", Workflow.End);

        var result = await workflow.RunAsync(new MiddlewareState { Data = "Q3 revenue: $1.2M, up 15%." });
        Console.WriteLine($"  Summary: {result.State.Summary}");
        Console.WriteLine($"  Status:  {result.Status} (guardrail passed)");
        Console.WriteLine();
    }
}

internal record MiddlewareState
{
    public string Data { get; init; } = "";
    public string Summary { get; init; } = "";
}

internal record SummaryResponse
{
    public string? Summary { get; init; }
}

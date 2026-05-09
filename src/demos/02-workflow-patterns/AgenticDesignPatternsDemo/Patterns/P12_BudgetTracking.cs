using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Agents;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P12_BudgetTracking
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("12. Budget / Cost Tracking");

        var model = SimulatedModel.Json(
            new { Result = "analysis complete" },
            inputTokens: 500,
            outputTokens: 200);

        var agentA = AgentJobFactory.Create<BudgetState, BudgetResponse>("agent-a", model)
            .WithPrompt(s => "Analyze data set A")
            .MapResult((s, r) => s with { StepA = r.Result ?? "" })
            .Build();

        var agentB = AgentJobFactory.Create<BudgetState, BudgetResponse>("agent-b", model)
            .WithPrompt(s => "Analyze data set B")
            .MapResult((s, r) => s with { StepB = r.Result ?? "" })
            .Build();

        var agentC = AgentJobFactory.Create<BudgetState, BudgetResponse>("agent-c", model)
            .WithPrompt(s => "Final synthesis")
            .MapResult((s, r) => s with { StepC = r.Result ?? "" })
            .Build();

        var workflow = new Workflow<BudgetState>("budget-demo")
            .Job("agent-a", agentA)
            .Job("agent-b", agentB)
            .Job("agent-c", agentC)
            .Chain("agent-a", "agent-b", "agent-c", Workflow.End)
            .WithBudget(
                maxCost: 0.01m,
                costPer1KInputTokens: 0.003m,
                costPer1KOutputTokens: 0.006m);

        var result = await workflow.RunAsync(new BudgetState());
        Console.WriteLine($"  Status:         {result.Status}");
        Console.WriteLine($"  Estimated cost: ${result.EstimatedCost:F6}");
        Console.WriteLine($"  Total tokens:   {result.CumulativeUsage.TotalTokens}");
        Console.WriteLine($"    Input:        {result.CumulativeUsage.InputTokens}");
        Console.WriteLine($"    Output:       {result.CumulativeUsage.OutputTokens}");
        Console.WriteLine($"  Jobs completed: {result.History.Count}");

        Console.WriteLine();
        Console.WriteLine("  [Budget exceeded scenario — tight budget]");
        var tightWorkflow = new Workflow<BudgetState>("budget-tight")
            .Job("agent-a", agentA)
            .Job("agent-b", agentB)
            .Job("agent-c", agentC)
            .Chain("agent-a", "agent-b", "agent-c", Workflow.End)
            .WithBudget(
                maxCost: 0.003m,
                costPer1KInputTokens: 0.003m,
                costPer1KOutputTokens: 0.006m);

        var tightResult = await tightWorkflow.RunAsync(new BudgetState());
        Console.WriteLine($"  Status:         {tightResult.Status}");
        Console.WriteLine($"  Estimated cost: ${tightResult.EstimatedCost:F6}");
        Console.WriteLine($"  Error:          {tightResult.Result?.Error}");
        Console.WriteLine();
    }
}

internal record BudgetState
{
    public string StepA { get; init; } = "";
    public string StepB { get; init; } = "";
    public string StepC { get; init; } = "";
}

internal record BudgetResponse
{
    public string? Result { get; init; }
}

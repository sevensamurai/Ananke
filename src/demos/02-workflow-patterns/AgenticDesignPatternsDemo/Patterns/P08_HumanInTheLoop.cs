using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P08_HumanInTheLoop
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("8. Human-in-the-Loop (interrupt + resume)");

        var checkpointStore = new InMemoryCheckpointStore();

        var workflow = new Workflow<ApprovalState>("approval-flow")
            .Job("analyze", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Analysis = $"Trade analysis for: {state.Request}" };
            })
            .Job("execute", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with
                {
                    Result = state.Approved
                        ? $"Executed: {state.Analysis}"
                        : $"Rejected: {state.Analysis}"
                };
            })
            .Then("analyze", "execute")
            .Then("execute", Workflow.End)
            .InterruptAfter("analyze")
            .UseCheckpointing(checkpointStore);

        var execution = await workflow.RunAsync(new ApprovalState { Request = "Buy 100 AAPL" });
        Console.WriteLine($"  Status:   {execution.Status}");
        Console.WriteLine($"  Analysis: {execution.State.Analysis}");

        var resumed = await workflow.ResumeAsync(
            execution.Id,
            state => state with { Approved = true });

        Console.WriteLine($"  Resumed:  {resumed.Status}");
        Console.WriteLine($"  Result:   {resumed.State.Result}");
        Console.WriteLine();
    }
}

internal record ApprovalState
{
    public string Request { get; init; } = "";
    public string Analysis { get; init; } = "";
    public bool Approved { get; init; }
    public string Result { get; init; } = "";
}

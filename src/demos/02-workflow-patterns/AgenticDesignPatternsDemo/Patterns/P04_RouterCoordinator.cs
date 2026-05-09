using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P04_RouterCoordinator
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("4. Router / Coordinator (dynamic dispatch)");

        var workflow = new Workflow<RouterState>("smart-router")
            .Job("classify", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                var category = state.Input.Contains("code", StringComparison.OrdinalIgnoreCase)
                    ? "technical" : "general";
                return state with { Category = category };
            })
            .Job("technical-agent", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Response = $"[Technical] Here's a code solution for: {state.Input}" };
            })
            .Job("general-agent", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Response = $"[General] Here's information about: {state.Input}" };
            })
            .Then("classify", Workflow.Decide<RouterState>(state =>
                state.Category == "technical" ? "technical-agent" : "general-agent"))
            .Then("technical-agent", Workflow.End)
            .Then("general-agent", Workflow.End);

        var result = await workflow.RunAsync(new RouterState { Input = "Write code for sorting" });
        Console.WriteLine($"  Input:    \"{result.State.Input}\"");
        Console.WriteLine($"  Category: {result.State.Category}");
        Console.WriteLine($"  Response: {result.State.Response}");
        Console.WriteLine();
    }
}

internal record RouterState
{
    public string Input { get; init; } = "";
    public string Category { get; init; } = "";
    public string Response { get; init; } = "";
}

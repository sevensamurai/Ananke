using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P02_SequentialChain
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("2. Sequential Chain");

        var workflow = new Workflow<PipelineState>("content-pipeline")
            .Job("research", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Research = "AI agents are software that act autonomously." };
            })
            .Job("draft", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Draft = $"Article based on: {state.Research}" };
            })
            .Job("edit", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { FinalOutput = $"[Polished] {state.Draft}" };
            })
            .Chain("research", "draft", "edit", Workflow.End);

        var result = await workflow.RunAsync(new PipelineState());
        Console.WriteLine($"  Research: {result.State.Research}");
        Console.WriteLine($"  Draft:    {result.State.Draft}");
        Console.WriteLine($"  Final:    {result.State.FinalOutput}");
        Console.WriteLine($"  Jobs run: {result.History.Count}");
        Console.WriteLine();
    }
}

internal record PipelineState
{
    public string Research { get; init; } = "";
    public string Draft { get; init; } = "";
    public string FinalOutput { get; init; } = "";
}

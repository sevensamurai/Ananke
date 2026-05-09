using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P09_SubFlowComposition
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("9. SubFlow Composition (nested workflows)");

        var innerIteration = 0;
        var innerWorkflow = new Workflow<InnerState>("inner-review")
            .Job("review", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                innerIteration++;
                return state with
                {
                    Output = $"[reviewed-v{innerIteration}] {state.Input}",
                    Score = Math.Min(1.0, 0.5 * innerIteration)
                };
            })
            .Loop("review", loopTarget: "review", exitTarget: Workflow.End,
                until: s => s.Score >= 0.9, maxIterations: 3);

        var outerWorkflow = new Workflow<OuterState>("outer-pipeline")
            .Job("prepare", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Draft = "Raw content about AI patterns." };
            })
            .SubFlow("review-subflow", innerWorkflow,
                mapIn: outer => new InnerState { Input = outer.Draft },
                mapOut: (outer, inner) => outer with { FinalOutput = inner.Output })
            .Job("publish", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Published = true };
            })
            .Chain("prepare", "review-subflow", "publish", Workflow.End);

        var result = await outerWorkflow.RunAsync(new OuterState());
        Console.WriteLine($"  Draft:     {result.State.Draft}");
        Console.WriteLine($"  Reviewed:  {result.State.FinalOutput}");
        Console.WriteLine($"  Published: {result.State.Published}");
        Console.WriteLine();
    }
}

internal record InnerState
{
    public string Input { get; init; } = "";
    public string Output { get; init; } = "";
    public double Score { get; init; }
}

internal record OuterState
{
    public string Draft { get; init; } = "";
    public string FinalOutput { get; init; } = "";
    public bool Published { get; init; }
}

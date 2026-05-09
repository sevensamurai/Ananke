using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P05_LoopPrimitive
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("5. Loop Primitive (workflow-level cycle)");

        var workflow = new Workflow<LoopState>("retry-loop")
            .Job("attempt", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                var newAttempt = state.Attempt + 1;
                var quality = 0.3 * newAttempt;
                Console.WriteLine($"    Attempt {newAttempt}: quality = {quality:F1}");
                return state with { Attempt = newAttempt, Quality = quality };
            })
            .Loop("attempt",
                loopTarget: "attempt",
                exitTarget: Workflow.End,
                until: s => s.Quality >= 0.9,
                maxIterations: 5);

        var result = await workflow.RunAsync(new LoopState());
        Console.WriteLine($"  Final quality: {result.State.Quality:F1} after {result.State.Attempt} attempts");
        Console.WriteLine();
    }
}

internal record LoopState
{
    public int Attempt { get; init; }
    public double Quality { get; init; }
}

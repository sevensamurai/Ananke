using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P07_IterativeRefinement
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("7. Iterative Refinement (self-improvement loop)");

        var round = 0;
        var refineAgent = AgentJobFactory.Create<RefinementState, RefinementResponse>("refine",
                SimulatedModel.Json(new RefinementResponse { Output = "Refined output." }))
            .WithPrompt(s => $"Improve this output: {s.Output}")
            .MapResult((s, r) =>
            {
                round++;
                var quality = Math.Min(1.0, 0.25 * round);
                Console.WriteLine($"    Round {round}: quality = {quality:F2}");
                return s with { Output = $"[r{round}] {r.Output}", Quality = quality };
            })
            .Build();

        var workflow = AgenticPattern.IterativeRefinement<RefinementState>("polish")
            .WithAgent(refineAgent)
            .Until(s => s.Quality >= 0.95)
            .MaxIterations(8)
            .Build();

        var result = await workflow.RunAsync(new RefinementState { Output = "Initial rough draft." });
        Console.WriteLine($"  Final output:  {result.State.Output}");
        Console.WriteLine($"  Final quality: {result.State.Quality:F2}");
        Console.WriteLine();
    }
}

internal record RefinementState
{
    public string Output { get; init; } = "";
    public double Quality { get; init; }
}

internal record RefinementResponse
{
    public string? Output { get; init; }
}

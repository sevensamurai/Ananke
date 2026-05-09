using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P06_ReviewCritique
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("6. Review & Critique (generator-critic loop)");

        var iteration = 0;

        var generator = AgentJobFactory.Create<ArticleState, ArticleGenResponse>("generator",
                SimulatedModel.Json(new ArticleGenResponse { Draft = "AI agents can autonomously perform tasks." }))
            .WithPrompt(s => $"Write an article about: {s.Topic}. Current draft: {s.Draft}")
            .MapResult((s, r) =>
            {
                iteration++;
                return s with { Draft = $"[v{iteration}] {r.Draft}" };
            })
            .Build();

        var critic = AgentJobFactory.Create<ArticleState, ArticleCritiqueResponse>("critic",
                SimulatedModel.Json(new ArticleCritiqueResponse { Score = 0.0, Feedback = "Needs more depth." }))
            .WithPrompt(s => $"Critique this draft (0-1 score): {s.Draft}")
            .MapResult((s, r) =>
            {
                var score = Math.Min(1.0, 0.3 * iteration);
                Console.WriteLine($"    Critic score: {score:F1} - {r.Feedback}");
                return s with { Score = score, Feedback = r.Feedback ?? "" };
            })
            .Build();

        var workflow = AgenticPattern.ReviewCritique<ArticleState>("article-review")
            .WithGenerator(generator)
            .WithCritic(critic)
            .Until(s => s.Score >= 0.9)
            .MaxIterations(5)
            .Build();

        var result = await workflow.RunAsync(new ArticleState { Topic = "AI Agents" });
        Console.WriteLine($"  Final draft: {result.State.Draft}");
        Console.WriteLine($"  Final score: {result.State.Score:F1}");
        Console.WriteLine();
    }
}

internal record ArticleState
{
    public string Topic { get; init; } = "";
    public string Draft { get; init; } = "";
    public double Score { get; init; }
    public string Feedback { get; init; } = "";
}

internal record ArticleGenResponse
{
    public string? Draft { get; init; }
}

internal record ArticleCritiqueResponse
{
    public double Score { get; init; }
    public string? Feedback { get; init; }
}

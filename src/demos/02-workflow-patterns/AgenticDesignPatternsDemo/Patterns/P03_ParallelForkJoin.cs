using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P03_ParallelForkJoin
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("3. Parallel Fork/Join");

        var workflow = new Workflow<ParallelState>("parallel-research")
            .Job("split", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Topic = "AI Safety" };
            })
            .Job("research-papers", async (state, ct) =>
            {
                await Task.Delay(50, ct);
                return state with { Papers = "Found 3 papers on AI alignment." };
            })
            .Job("research-news", async (state, ct) =>
            {
                await Task.Delay(30, ct);
                return state with { News = "EU AI Act passed; OpenAI announces safety board." };
            })
            .Job("synthesize", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return state with { Summary = $"Papers: {state.Papers} | News: {state.News}" };
            })
            .Then("split", Workflow.Fork("research-papers", "research-news"))
            .Join(["research-papers", "research-news"], "synthesize",
                states =>
                {
                    var papers = states.FirstOrDefault(s => s.Papers is not null)?.Papers ?? "";
                    var news = states.FirstOrDefault(s => s.News is not null)?.News ?? "";
                    return states[0] with { Papers = papers, News = news };
                })
            .Then("synthesize", Workflow.End);

        var result = await workflow.RunAsync(new ParallelState());
        Console.WriteLine($"  Papers: {result.State.Papers}");
        Console.WriteLine($"  News:   {result.State.News}");
        Console.WriteLine($"  Summary: {result.State.Summary}");
        Console.WriteLine();
    }
}

internal record ParallelState
{
    public string Topic { get; init; } = "";
    public string? Papers { get; init; }
    public string? News { get; init; }
    public string Summary { get; init; } = "";
}

using AgenticDesignPatternsDemo;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P11_ContextStrategy
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("11. Context Strategy (sliding window)");

        var messages = new List<AgentMessage>();
        for (var i = 1; i <= 20; i++)
            messages.Add(AgentMessage.User($"Message {i}: " + new string('x', 100)));

        var strategy = new SlidingWindowContextStrategy(maxTokens: 500);
        var compacted = await strategy.ApplyAsync(messages, "You are a helpful assistant.");

        Console.WriteLine($"  Original messages: {messages.Count}");
        Console.WriteLine($"  After compaction:  {compacted.Count}");
        Console.WriteLine($"  Strategy: SlidingWindowContextStrategy(maxTokens: 500)");

        var model = SimulatedModel.Fixed("""{"Reply":"I remember the recent context."}""");
        var agent = AgentJobFactory.Create<ContextState, ContextResponse>("chat", model)
            .WithSystemPrompt("You are a helpful assistant.")
            .WithPrompt(s => s.UserMessage)
            .WithContextStrategy(strategy)
            .MapResult((s, r) => s with { Reply = r.Reply ?? "" })
            .Build();

        var workflow = new Workflow<ContextState>("context-demo")
            .Job("chat", agent)
            .Then("chat", Workflow.End);

        var result = await workflow.RunAsync(new ContextState { UserMessage = "What did we discuss?" });
        Console.WriteLine($"  Reply:   {result.State.Reply}");
        Console.WriteLine();
    }
}

internal record ContextState
{
    public string UserMessage { get; init; } = "";
    public string Reply { get; init; } = "";
}

internal record ContextResponse
{
    public string? Reply { get; init; }
}

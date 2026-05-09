using AgenticDesignPatternsDemo;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P01_SingleAgent
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("1. Single Agent (ReAct tool-calling loop)");

        var model = SimulatedModel.Fixed("""{"Answer":"The weather in Seattle is sunny and 22 C - great for a walk!"}""");

        var tools = new ToolKit("weather")
            .AddTool("get_weather", "Gets current weather for a city",
                (string city) => ToolResult.Ok($"Sunny, 22 C in {city}"),
                "city", "City name");

        var agent = AgentJobFactory.Create<AgentState, AgentReply>("weather-agent", model)
            .WithSystemPrompt("You are a helpful weather assistant.")
            .WithPrompt(s => s.UserInput)
            .WithTools(tools)
            .MapResult((s, r) => s with { Output = r.Answer ?? "" })
            .Build();

        var workflow = new Workflow<AgentState>("single-agent")
            .Job("agent", agent)
            .Then("agent", Workflow.End);

        var result = await workflow.RunAsync(new AgentState { UserInput = "What's the weather in Seattle?" });
        Console.WriteLine($"  Output: {result.State.Output}");
        Console.WriteLine($"  Status: {result.Status}");
        Console.WriteLine();
    }
}

internal record AgentState
{
    public string UserInput { get; init; } = "";
    public string Output { get; init; } = "";
}

internal record AgentReply
{
    public string? Answer { get; init; }
}

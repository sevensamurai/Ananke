using AgenticDesignPatternsDemo;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tools;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P13_StreamingChat
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("13. Streaming Chat (StreamingChatWorkflow)");

        var model = SimulatedModel.Fixed("The capital of France is Paris. It's known for the Eiffel Tower.");

        var tools = new ToolKit("geography")
            .AddTool("get_capital", "Gets the capital of a country",
                (string country) => ToolResult.Ok($"The capital of {country} is Paris."),
                "country", "Country name");

        Console.Write("  Streaming: ");
        await foreach (var evt in StreamingChatWorkflow.Create("chat", model)
            .WithSystemPrompt("You are a geography expert.")
            .WithTools(tools)
            .OnTextDelta(async delta => { Console.Write(delta); await Task.CompletedTask; })
            .BuildStream([AgentMessage.User("What is the capital of France?")]))
        {
            switch (evt)
            {
                case CompletedEvent completed:
                    Console.WriteLine();
                    Console.WriteLine($"  Completed: {completed.FullText?.Length ?? 0} chars");
                    break;
                case ErrorEvent error:
                    Console.WriteLine($"  Error: {error.Message}");
                    break;
            }
        }
        Console.WriteLine();
    }
}

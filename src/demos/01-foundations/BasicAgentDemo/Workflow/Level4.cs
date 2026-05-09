using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tracing;
using Ananke.OpenTelemetry;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.OpenAI;
using BasicAgentDemo.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace BasicAgentDemo.Workflow;

/// <summary>
/// Level 4 — Streaming chat workflow with stock tools and OpenTelemetry tracing.
/// Absorbed from SimpleWorkflowDemo.
/// </summary>
internal static class Level4
{
    internal static async Task RunAsync(IConfiguration config)
    {
        var otlpEndpoint = config["BetterStack:OtlpEndpoint"]
            ?? throw new InvalidOperationException("BetterStack:OtlpEndpoint missing");
        var otlpToken = config["BetterStack:OtlpSourceToken"]
            ?? throw new InvalidOperationException("BetterStack:OtlpSourceToken missing");
        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey missing");
        var modelName = config["OpenAI:Model"] ?? "gpt-4.1-mini";

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var services = new ServiceCollection();
        services.AddTracingPipeline(o =>
        {
            o.ServiceName = "BasicAgentDemo-Workflow";
            o.ServiceVersion = "0.1.0";
            o.UseOtlp(otlpEndpoint, $"Authorization=Bearer {otlpToken}");
        });

        using var sp = services.BuildServiceProvider();
        var tracer = sp.GetRequiredService<IWorkflowTracer>();
        var tracerProvider = sp.GetRequiredService<TracerProvider>();

        Console.WriteLine($"[OTel] endpoint={otlpEndpoint}");

        IStreamingAgentModel model = OpenAIChatAgentModel.Create(apiKey, modelName);
        var stockTools = StockTools.Create();

        const string systemPrompt = """
            You are a helpful stock market assistant. You can look up stock data and execute trades.
            The user starts with $100,000 cash. Available stocks: AAPL, MSFT, GOOGL, AMZN, TSLA, NVDA, META, JPM.
            Answer concisely.
            """;

        Console.WriteLine($"[Model] {modelName}");
        Console.WriteLine("Ask a stock question (or 'quit'):");
        Console.WriteLine();

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) continue;
            if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

            var messages = new List<AgentMessage> { AgentMessage.User(input) };

            var execution = await StreamingChatWorkflow.Create("stock-chat", model)
                .WithSystemPrompt(systemPrompt)
                .WithTools(stockTools)
                .OnTextDelta(delta => { Console.Write(delta); return Task.CompletedTask; })
                .OnToolResult((name, result) =>
                {
                    Console.WriteLine($"\n  [{name}] {result}");
                    return Task.CompletedTask;
                })
                .Build()
                .UseTracing(tracer)
                .RunAsync(new StreamingChatState { Messages = messages });

            Console.WriteLine();
            Console.WriteLine($"[{execution.Status}]");
            Console.WriteLine();
        }

        tracerProvider.ForceFlush(5_000);
    }
}

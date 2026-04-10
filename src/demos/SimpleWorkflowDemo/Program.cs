using System.Diagnostics;
using Ananke.Abstractions.Tracing;
using Ananke.OpenTelemetry;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using OpenTelemetry.Trace;
using System.ClientModel;

// --- 1. Config ---
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("secrets.json", optional: true)
    .Build();

var otlpEndpoint = config["BetterStack:OtlpEndpoint"]
    ?? throw new InvalidOperationException("BetterStack:OtlpEndpoint missing");
var otlpToken = config["BetterStack:OtlpSourceToken"]
    ?? throw new InvalidOperationException("BetterStack:OtlpSourceToken missing");
var apiKey = config["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey missing");
var modelName = config["OpenAI:Model"] ?? "gpt-4.1-mini";

// --- 2. DI setup (same pattern as the working reference) ---
Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

var services = new ServiceCollection();

services.AddTracingPipeline(o =>
{
    o.ServiceName = "SimpleWorkflow";
    o.ServiceVersion = "0.1.0";
    o.UseOtlp(otlpEndpoint, $"Authorization=Bearer {otlpToken}");
});

using var sp = services.BuildServiceProvider();
var tracer = sp.GetRequiredService<IWorkflowTracer>();
var tracerProvider = sp.GetRequiredService<TracerProvider>();

Console.WriteLine($"[OTel] endpoint={otlpEndpoint}");

// --- 3. AI model + tools ---
IStreamingAgentModel model = new OpenAIChatAgentModel(
    new ChatClient(modelName, new ApiKeyCredential(apiKey)));

var stockTools = StockTools.Create();

const string systemPrompt = """
    You are a helpful stock market assistant. You can look up stock data and execute trades.
    The user starts with $100,000 cash. Available stocks: AAPL, MSFT, GOOGL, AMZN, TSLA, NVDA, META, JPM.
    Answer concisely.
    """;

// --- 4. Interactive loop ---
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

// Flush remaining spans before exit.
tracerProvider.ForceFlush(5_000);

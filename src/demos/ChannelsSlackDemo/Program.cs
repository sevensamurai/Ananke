using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Platforms;
using Ananke.Platforms.Sessions;
using Ananke.Platforms.Slack;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ---------------------------------------------------------------------
//  ChannelsSlackDemo — Ananke agent exposed as a Slack bot
//
//  Prerequisites:
//    1. Create a Slack App at https://api.slack.com/apps
//    2. Enable Socket Mode → generate an App-Level Token (xapp-…)
//    3. Add Bot Token Scopes: chat:write, reactions:write, app_mentions:read,
//       channels:history, groups:history, im:history, mpim:history
//    4. Install the app to your workspace → copy Bot User OAuth Token (xoxb-…)
//    5. Create secrets.json (see README.md)
//
//  Usage:
//    dotnet run
// ---------------------------------------------------------------------

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

// --- Resolve config ---
var openAiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey not found. Add it to secrets.json or environment variables.");
var openAiModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4.1-mini";
var slackBotToken = builder.Configuration["Slack:BotToken"]
    ?? throw new InvalidOperationException("Slack:BotToken not found. Add it to secrets.json or environment variables.");
var slackAppToken = builder.Configuration["Slack:AppToken"]
    ?? throw new InvalidOperationException("Slack:AppToken not found. Add it to secrets.json or environment variables.");

// --- Register the LLM model ---
var model = OpenAIChatAgentModel.Create(openAiKey, openAiModel);
builder.Services.AddSingleton<IStreamingAgentModel>(model);

// --- Register tools (optional — add your own) ---
var tools = new ToolKit("slack-tools")
    .AddTool("current_time", "Returns the current UTC date and time.",
        () => ToolResult.Ok(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")))
    .AddTool("echo", "Echoes the input text back.", b => b
        .Param("text", "The text to echo")
        .OnExecute(async args => ToolResult.Ok($"Echo: {args.Get("text")}")));

builder.Services.AddSingleton(tools);

// --- Register conversation memory (session-aware, auto-persisted) ---
builder.Services.AddSingleton<IConversationMemory>(new InMemoryConversationMemory(ttl: TimeSpan.FromHours(1)));

// --- Register the Slack adapter ---
builder.Services.AddAnankeSlack(options =>
{
    options.BotToken = slackBotToken;
    options.AppToken = slackAppToken;
    options.UseSocketMode = true;
});

// --- Register the message handler ---
builder.Services.AddSingleton<IPlatformMessageHandler, SlackAgentHandler>();

// --- Run ---
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Ananke — Slack Bot Demo (Socket Mode)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine($"  Model: {openAiModel}");
Console.WriteLine("  Send a message to your bot in Slack to start chatting.");
Console.WriteLine("  Press Ctrl+C to stop.");
Console.WriteLine();

var host = builder.Build();
await host.RunAsync();

// ─────────────────────────────────────────────────────────────────────
//  Message handler — uses ConversationalMessageHandler for session-aware
//  memory-integrated streaming chat over Slack
// ─────────────────────────────────────────────────────────────────────

sealed class SlackAgentHandler(
    IStreamingAgentModel model,
    IConversationMemory memory,
    ToolKit tools)
    : ConversationalMessageHandler(model, memory, tools)
{
    protected override string? SystemPrompt =>
        "You are a helpful assistant in a Slack workspace. " +
        "Keep responses concise and use Slack-friendly formatting (bold, code blocks, lists).";

    protected override string WorkflowName => "slack-agent";

    protected override string GetSessionId(PlatformMessage message)
        => SessionKeyBuilder.Build(message, "slack");
}

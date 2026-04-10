using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Platforms;
using Ananke.Platforms.Sessions;
using Ananke.Platforms.Discord;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ---------------------------------------------------------------------
//  ChannelsDiscordDemo — Ananke agent exposed as a Discord bot
//
//  Prerequisites:
//    1. Create a Discord Application at https://discord.com/developers/applications
//    2. Under Bot → Reset Token → copy the bot token
//    3. Under Bot → Privileged Gateway Intents → enable Message Content Intent
//    4. Under OAuth2 → URL Generator → select scopes: bot
//       Bot permissions: Send Messages, Read Message History, Add Reactions
//    5. Use the generated URL to invite the bot to your server
//    6. Create secrets.json (see below)
//
//  secrets.json:
//    {
//      "OpenAI": { "ApiKey": "sk-…", "Model": "gpt-4.1-mini" },
//      "Discord": { "BotToken": "your-bot-token" }
//    }
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
var discordBotToken = builder.Configuration["Discord:BotToken"]
    ?? throw new InvalidOperationException("Discord:BotToken not found. Add it to secrets.json or environment variables.");

// --- Register the LLM model ---
var model = OpenAIChatAgentModel.Create(openAiKey, openAiModel);
builder.Services.AddSingleton<IStreamingAgentModel>(model);

// --- Register tools (optional — add your own) ---
var tools = new ToolKit("discord-tools")
    .AddTool("current_time", "Returns the current UTC date and time.",
        () => ToolResult.Ok(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")))
    .AddTool("echo", "Echoes the input text back.", b => b
        .Param("text", "The text to echo")
        .OnExecute(async args => ToolResult.Ok($"Echo: {args.Get("text")}")));

builder.Services.AddSingleton(tools);

// --- Register conversation memory (session-aware, auto-persisted) ---
builder.Services.AddSingleton<IConversationMemory>(new InMemoryConversationMemory(ttl: TimeSpan.FromHours(1)));

// --- Register the Discord adapter ---
builder.Services.AddAnankeDiscord(options =>
{
    options.BotToken = discordBotToken;
    options.SlashCommandTools = tools; // registers /current_time and /echo as slash commands
});

// --- Register the message handler ---
builder.Services.AddSingleton<IPlatformMessageHandler, DiscordAgentHandler>();

// --- Run ---
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Ananke — Discord Bot Demo (Gateway)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine($"  Model: {openAiModel}");
Console.WriteLine("  Send a message to your bot in Discord to start chatting.");
Console.WriteLine("  Press Ctrl+C to stop.");
Console.WriteLine();

var host = builder.Build();
await host.RunAsync();

// ─────────────────────────────────────────────────────────────────────
//  Message handler — uses ConversationalMessageHandler for session-aware
//  memory-integrated streaming chat over Discord
// ─────────────────────────────────────────────────────────────────────

sealed class DiscordAgentHandler(
    IStreamingAgentModel model,
    IConversationMemory memory,
    ToolKit tools)
    : ConversationalMessageHandler(model, memory, tools)
{
    protected override string? SystemPrompt =>
        "You are a helpful assistant in a Discord server. " +
        "Keep responses concise and use Discord-friendly formatting (bold, code blocks, lists).";

    protected override string WorkflowName => "discord-agent";

    protected override string GetSessionId(PlatformMessage message)
        => SessionKeyBuilder.Build(message, "discord");
}

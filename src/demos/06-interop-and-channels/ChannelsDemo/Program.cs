using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Ananke.Platforms;
using Ananke.Platforms.Discord;
using Ananke.Platforms.Sessions;
using Ananke.Platforms.Slack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ---------------------------------------------------------------------
//  ChannelsDemo — Ananke agent exposed as a Discord or Slack bot
//
//  Usage:
//    dotnet run -- --platform discord
//    dotnet run -- --platform slack
//
//  Discord prerequisites:
//    1. Create a Discord Application at https://discord.com/developers/applications
//    2. Under Bot → Reset Token → copy the bot token
//    3. Under Bot → Privileged Gateway Intents → enable Message Content Intent
//    4. Under OAuth2 → URL Generator → select scopes: bot
//       Bot permissions: Send Messages, Read Message History, Add Reactions
//    5. Use the generated URL to invite the bot to your server
//
//  Slack prerequisites:
//    1. Create a Slack App at https://api.slack.com/apps
//    2. Enable Socket Mode → generate an App-Level Token (xapp-…)
//    3. Add Bot Token Scopes: chat:write, reactions:write, app_mentions:read,
//       channels:history, groups:history, im:history, mpim:history
//    4. Install the app to your workspace → copy Bot User OAuth Token (xoxb-…)
//
//  secrets.json (Discord):
//    { "OpenAI": { "ApiKey": "sk-…", "Model": "gpt-4.1-mini" },
//      "Discord": { "BotToken": "your-bot-token" } }
//
//  secrets.json (Slack):
//    { "OpenAI": { "ApiKey": "sk-…", "Model": "gpt-4.1-mini" },
//      "Slack": { "BotToken": "xoxb-…", "AppToken": "xapp-…" } }
// ---------------------------------------------------------------------

var platform = ParsePlatform(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

var openAiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey not found. Add it to secrets.json or environment variables.");
var openAiModel = builder.Configuration["OpenAI:Model"] ?? Models.OpenAI.Gpt54Mini;

var model = OpenAIChatAgentModel.Create(openAiKey, openAiModel);
builder.Services.AddSingleton<IStreamingAgentModel>(model);

var tools = new ToolKit($"{platform}-tools")
    .AddTool("current_time", "Returns the current UTC date and time.",
        () => ToolResult.Ok(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")))
    .AddTool("echo", "Echoes the input text back.", b => b
        .Param("text", "The text to echo")
        .OnExecute(async args => ToolResult.Ok($"Echo: {args.Get("text")}")));

builder.Services.AddSingleton(tools);
builder.Services.AddSingleton<IConversationMemory>(new InMemoryConversationMemory(ttl: TimeSpan.FromHours(1)));

if (platform == "discord")
{
    var discordBotToken = builder.Configuration["Discord:BotToken"]
        ?? throw new InvalidOperationException("Discord:BotToken not found. Add it to secrets.json.");

    builder.Services.AddAnankeDiscord(options =>
    {
        options.BotToken = discordBotToken;
        options.SlashCommandTools = tools;
    });
    builder.Services.AddSingleton<IPlatformMessageHandler, DiscordAgentHandler>();
}
else
{
    var slackBotToken = builder.Configuration["Slack:BotToken"]
        ?? throw new InvalidOperationException("Slack:BotToken not found. Add it to secrets.json.");
    var slackAppToken = builder.Configuration["Slack:AppToken"]
        ?? throw new InvalidOperationException("Slack:AppToken not found. Add it to secrets.json.");

    builder.Services.AddAnankeSlack(options =>
    {
        options.BotToken = slackBotToken;
        options.AppToken = slackAppToken;
        options.UseSocketMode = true;
    });
    builder.Services.AddSingleton<IPlatformMessageHandler, SlackAgentHandler>();
}

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine($"  Ananke — Channels Demo ({platform})");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine($"  Model:    {openAiModel}");
Console.WriteLine($"  Platform: {platform}");
Console.WriteLine("  Send a message to your bot to start chatting.");
Console.WriteLine("  Press Ctrl+C to stop.");
Console.WriteLine();

var host = builder.Build();
await host.RunAsync();

static string ParsePlatform(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].Equals("--platform", StringComparison.OrdinalIgnoreCase))
            return args[i + 1].ToLowerInvariant();

    Console.Error.WriteLine("Usage: dotnet run -- --platform discord|slack");
    Console.Error.WriteLine("Defaulting to --platform discord");
    return "discord";
}

// ─────────────────────────────────────────────────────────────────────
//  Discord handler
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

// ─────────────────────────────────────────────────────────────────────
//  Slack handler
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

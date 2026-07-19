using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Ananke.Skills;
using Ananke.Skills.OpenClaw;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.ClientModel;

namespace LearningPrimitivesDemo.Skills;

// ─────────────────────────────────────────────────────────────────────
//  Skills scenario — discovers and uses external CLI skills via the
//  Ananke skill catalog. Uses cowsay to validate the full pipeline.
//
//  Prerequisites:
//    - uv installed: winget install astral-sh.uv
//    - OpenAI API key in secrets.json: { "OpenAI": { "ApiKey": "sk-..." } }
//
//  See: docs/guides/uv-setup-for-dotnet-developers.md
// ─────────────────────────────────────────────────────────────────────

internal static class SkillsScenario
{
    internal static async Task RunAsync(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("secrets.json", optional: true)
            .Build();

        var apiKey = config["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey missing from secrets.json");

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
                   .SetMinimumLevel(LogLevel.Debug));

        var cacheDir = Path.Combine(AppContext.BaseDirectory, "skill-cache");
        var scoresPath = Path.Combine(cacheDir, "scores.json");

        if (File.Exists(scoresPath))
            File.Delete(scoresPath);

        var scoreStore = new JsonFileScoreStore(scoresPath);
        var catalog = new OpenClawCatalog(cacheDir, scoreStore,
            enableVoting: true,
            logger: loggerFactory.CreateLogger<OpenClawCatalog>());

        await catalog.AddSkillsAsync([
            new SkillDescriptor
            {
                Id = "python/cowsay",
                Name = "cowsay",
                Description = "Display text as ASCII art spoken by a character. Supports cow, tux, dragon, fox, trex, stegosaurus, turtle, kitty, and more.",
                Tags = ["ascii", "art", "fun", "text", "creative"],
                Install = SkillInstallMethod.Uvx,
                InstallPackage = "cowsay",
                Parameters =
                [
                    new("text", "The text to display in ASCII art", IsRequired: true),
                    new("character", "Character to use: cow, tux, dragon, fox, trex, stegosaurus, turtle, kitty, pig, octopus, cheese, daemon")
                ]
            }
        ]);

        Console.WriteLine("✓ Skill catalog seeded");

        var toolkit = await new ToolKit("creative")
            .AddFromCatalogAsync(catalog, "ascii art text", limit: 3);

        Console.WriteLine($"✓ Toolkit loaded: {toolkit.Tools.Count} tool(s)");
        foreach (var (name, tool) in toolkit.Tools)
            Console.WriteLine($"  • {name}: {tool.Description[..Math.Min(80, tool.Description.Length)]}");
        Console.WriteLine();

        var prereqCheck = await toolkit.CheckPrerequisitesAsync();
        if (!prereqCheck.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("✗ Prerequisites not met:");
            Console.WriteLine(prereqCheck.Summary);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Install uv: winget install astral-sh.uv");
            Console.WriteLine("See: docs/guides/uv-setup-for-dotnet-developers.md");
            return;
        }

        Console.WriteLine("✓ All prerequisites satisfied");
        Console.WriteLine();

        IAgentModel model = new OpenAIChatAgentModel(
            new ChatClient(Models.OpenAI.Gpt54Mini, new ApiKeyCredential(apiKey)));

        var agent = new AgentJob<DemoState, DemoResult>.Builder("ascii-artist", model)
            .WithSystemPrompt("""
                You are a creative ASCII art assistant. Use the cowsay tool to generate
                ASCII art for the user. Pick a character that matches the mood or topic.
                Return the raw ASCII art output in your summary — do not describe it,
                just include the art itself.
                """)
            .WithPrompt(s => s.Query)
            .WithTools(toolkit)
            .WithLogger(loggerFactory)
            .MapResult((s, r) => s with { Result = r })
            .Build();

        Console.WriteLine("── Generating ASCII art... ──");
        Console.WriteLine();

        var state = new DemoState("Make a dancing octopus");
        var result = await agent.ExecuteAsync(state);

        Console.WriteLine(result.Result?.Summary ?? "No result.");
        Console.WriteLine();

        var score = await scoreStore.GetScoreAsync("python/cowsay");
        Console.WriteLine($"Skill score for cowsay: ↑{score.UpVotes} ↓{score.DownVotes} (net: {score.Net})");
    }
}

file record DemoState(string Query, DemoResult? Result = null);

file record DemoResult
{
    public string Summary { get; init; } = string.Empty;
}

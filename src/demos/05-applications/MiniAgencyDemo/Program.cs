using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Design;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Division.Approval;
using Ananke.Platforms;
using Ananke.Platforms.Slack;
using Ananke.Roles.Roles;
using Ananke.Roles.Studio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using MiniAgencyDemo;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

var options = MiniAgencyOptions.Load(builder.Configuration);
var demoRoot = AppContext.BaseDirectory;
var rolesPath = Path.Combine(demoRoot, "roles.json");
var workflowPath = Path.Combine(demoRoot, "build-and-review.ananke.yml");
var roles = MiniAgencyRoleLoader.Load(rolesPath);
var model = OpenAIChatAgentModel.Create(options.LocalApiKey, options.LocalModelName, options.LocalEndpoint);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(model);
builder.Services.AddSingleton<IAgentModel>(sp => sp.GetRequiredService<OpenAIChatAgentModel>());
builder.Services.AddSingleton<IStreamingAgentModel>(sp => sp.GetRequiredService<OpenAIChatAgentModel>());
builder.Services.AddSingleton<IConversationMemory>(new InMemoryConversationMemory(ttl: TimeSpan.FromHours(8)));
builder.Services.AddSingleton(new InMemoryBudgetMeter(options.BudgetWindow));
builder.Services.AddSingleton<IBudgetMeter>(sp => sp.GetRequiredService<InMemoryBudgetMeter>());
builder.Services.AddSingleton(new MiniAgencyBudgetMetrics(options.EnableBudgetMetrics));
builder.Services.AddSingleton(CreateToolKit());
builder.Services.AddSingleton<IPlatformMessageHandler, MiniAgencyMessageHandler>();

builder.Services.AddAnankeSlack(slack =>
{
    slack.BotToken = options.SlackBotToken;
    slack.AppToken = options.SlackAppToken;
    slack.SigningSecret = options.SlackSigningSecret;
    slack.UseSocketMode = options.UseSocketMode;
    slack.EnableAppMentions = true;
    slack.EnableReactions = true;
    slack.StreamingOptions = new StreamingBridgeOptions
    {
        DebounceInterval = TimeSpan.FromMilliseconds(500),
        ThinkingPlaceholder = "Drafting response..."
    };
});

var studio = new StudioHostBuilder()
    .WithOptions(new StudioOptions
    {
        ModelAliasMap = new Dictionary<string, ModelDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["local"] = new()
            {
                Provider = "openai",
                Model = options.LocalModelName,
                Endpoint = options.LocalEndpoint.ToString()
            }
        },
        PerRoleTokenBudgetCaps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [options.WorkflowName] = options.BudgetCap
        }
    })
    .DisableDivision();

foreach (var role in roles)
    studio.AddRole(role);

studio.UseWorkflow(options.WorkflowName, workflowPath)
    .Build(builder.Services);

await builder.Build().RunAsync();

static ToolKit CreateToolKit() => new ToolKit("mini-agency")
    .AddTool(
        name: "current_time",
        description: "Returns the current UTC timestamp in ISO-8601 format.",
        execute: () => ToolResult.Ok(DateTimeOffset.UtcNow.ToString("O")));

using System.Text;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Ananke.AspNetCore.Configuration;
using Ananke.AspNetCore.Sessions;
using Ananke.MQTT;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Knowledge;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Abstractions.Memory;
using Ananke.Redis;
using Microsoft.Extensions.Logging.Console;
using StackExchange.Redis;

Console.OutputEncoding = Encoding.UTF8;
MqttHandoff.Register();

// ── Mode selection ───────────────────────────────────────────────────
//   dotnet run                      → Web app (main process)
//   dotnet run -- --payment-service → Standalone MQTT payment listener
if (args.Contains("--payment-service", StringComparer.OrdinalIgnoreCase))
{
    await PaymentService.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);

builder.Logging
    .AddConsoleFormatter<MinimalConsoleFormatter, ConsoleFormatterOptions>()
    .AddConsole(o => o.FormatterName = MinimalConsoleFormatter.FormatterName);

// --- Register LLM providers & validate config before any work ---
var modelFactory = ProviderRegistration.CreateFactory();

ProviderProfile settings;
try
{
    settings = modelFactory.FromConfiguration(builder.Configuration);
}
catch (InvalidOperationException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"\n❌ {ex.Message}");
    Console.Error.WriteLine("   Create a secrets.json file in the project directory with your provider config.");
    Console.Error.WriteLine("""   Example: { "Provider": "OpenAI", "OpenAI": { "ApiKey": "YOUR_KEY" } }""");
    Console.ResetColor();
    return;
}

// ── MQTT / Redis / Qdrant configuration ─────────────────────────────
var mqttHost = builder.Configuration["Mqtt:Host"];
var mqttPort = int.TryParse(builder.Configuration["Mqtt:Port"], out var mp) ? mp : 1883;
var mqttNamespace = builder.Configuration["Mqtt:Namespace"] ?? "handoff";

var redisHost = builder.Configuration["Redis:Host"];
var redisPort = int.TryParse(builder.Configuration["Redis:Port"], out var rp) ? rp : 6379;

var qdrantHost = builder.Configuration["Qdrant:Host"] ?? "localhost";
var qdrantPort = int.TryParse(builder.Configuration["Qdrant:Port"], out var qp) ? qp : 6334;

if (string.IsNullOrWhiteSpace(mqttHost) || string.IsNullOrWhiteSpace(redisHost))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("\n❌ MQTT and Redis are required. Set Mqtt:Host and Redis:Host in appsettings.json.");
    Console.Error.WriteLine("   Run 'docker compose up -d' to start the infrastructure containers.");
    Console.ResetColor();
    return;
}

var app = builder.Build();

// --- Static files ---
app.UseStaticFiles();

// --- Knowledge base bootstrap ---
var dataPath = Path.Combine(app.Environment.ContentRootPath, "data");
if (!Directory.Exists(dataPath) || Directory.GetFiles(dataPath, "*.md").Length == 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"\n❌ No markdown files found in {dataPath}");
    Console.Error.WriteLine("   The data/ directory with .md knowledge files is required.\n");
    Console.ResetColor();
    return;
}

var (knowledgeStore, petStore, catalog, _) = await IngestionWorkflow.RunAsync(dataPath, settings, qdrantHost, qdrantPort);
var knowledge = new KnowledgeBase(
    [new(ShelterKnowledge.General, knowledgeStore), new(ShelterKnowledge.Pets, petStore)],
    catalog);

// ── Conversation memory (Redis) ──────────────────────────────────────
var redis = await ConnectionMultiplexer.ConnectAsync($"{redisHost}:{redisPort}");
IConversationMemory memory = new RedisConversationMemory(redis, ttl: TimeSpan.FromHours(2));
Console.WriteLine("  ✓ Connected to Redis (conversation memory)");

// ── Handoff channel ─────────────────────────────────────────────────────
IHandoffChannel channel;
try
{
    channel = await HandoffChannel.ConnectAsync(new ChannelConfig
    {
        Host = mqttHost!,
        Port = mqttPort,
        Namespace = mqttNamespace
    });
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"  ✗ {ex.Message}");
    Console.ResetColor();
    return;
}
Console.WriteLine("  ✓ Connected to MQTT broker (payment handoff)");
Console.WriteLine("  ⚠ Make sure the payment service is running: dotnet run -- --payment-service");

var payments = Handoff.Proxy<PaymentHandoff, PaymentResult>(
    PaymentConstants.QueueName, channel, TimeSpan.FromSeconds(30));

// --- Session factory & store ---
var agentModel = settings.CreateAgentModel();
var factory = new SessionFactory(agentModel, knowledge, memory,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Session"));
var sessions = new InMemorySessionStore<AdoptionSession>();

// --- Endpoints ---
app.MapChatEndpoints(factory, sessions);
app.MapInterruptEndpoint(sessions);
app.MapPaymentEndpoint(sessions, payments, factory);
app.MapFallbackToFile("index.html");

// --- Startup summary ---
Console.WriteLine();
Console.WriteLine("🐾 Happy Tails Pet Adoption Demo");
Console.WriteLine(new string('─', 42));
Console.WriteLine($"  Provider:   {settings.Provider}");
Console.WriteLine($"  Model:      {settings.Model}");
Console.WriteLine($"  Embedder:   {settings.EmbeddingModel ?? "InMemoryEmbedder (local hash)"}");
Console.WriteLine($"  API key:    {settings.ApiKey[..8]}…{settings.ApiKey[^4..]}");
Console.WriteLine($"  Knowledge:  {Directory.GetFiles(dataPath, "*.md").Length} files indexed (Qdrant @ {qdrantHost}:{qdrantPort})");
Console.WriteLine($"  Handoff:    MQTT ({mqttHost}:{mqttPort})");
Console.WriteLine($"  Memory:     Redis ({redisHost}:{redisPort})");
Console.WriteLine($"  Phases:     Searching → Paperwork → Payment → Done");
Console.WriteLine($"  Endpoints:  POST /api/chat, POST /api/interrupt, POST /api/payment");
Console.WriteLine(new string('─', 42));
Console.WriteLine();

await app.RunAsync();

// ── Cleanup ──────────────────────────────────────────────────────────
await channel.DisposeAsync();
await redis.DisposeAsync();

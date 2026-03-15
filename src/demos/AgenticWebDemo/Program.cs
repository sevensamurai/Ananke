using Ananke.OpenTelemetry;
using Scalar.AspNetCore;

// --- 1. Configure services ---
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);
builder.Services.AddOpenApi();

var app = builder.Build();

// --- 1b. OpenTelemetry tracing (exports Ananke workflow spans to BetterStack) ---
var otlpEndpoint = app.Configuration["BetterStack:OtlpEndpoint"];
var otlpToken = app.Configuration["BetterStack:OtlpSourceToken"];
using var tracing = otlpToken is not null
    ? OtelTracingBuilder.Build(o =>
    {
        o.ServiceName = "AgenticWeb";
        if (otlpEndpoint is not null)
            o.UseOtlp(otlpEndpoint, $"Authorization=Bearer {otlpToken}");
        else
            o.UseBetterStack(otlpToken);
    })
    : null;

if (tracing is null)
    Console.WriteLine("[Ananke.OTel] Tracing is DISABLED — BetterStack:OtlpSourceToken not found in configuration.");

// --- 2. Configure middleware ---
app.UseStaticFiles();
app.MapOpenApi();
app.MapScalarApiReference();

// --- 3. Build the AI agent ---
var provider = AgentConfig.Configure(app.Configuration);
var agentModel = provider.CreateAgentModel();
var stockTools = StockTools.Create();

// --- 4. Register endpoints ---
app.MapChatEndpoint(agentModel, stockTools, tracing);
app.MapTradeApprovalEndpoints(agentModel, stockTools, tracing);
app.MapFallbackToFile("index.html");

// --- 5. Run ---
await app.RunAsync();

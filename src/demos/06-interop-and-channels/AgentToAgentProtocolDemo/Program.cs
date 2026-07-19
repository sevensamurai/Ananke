using System.Text;
using A2A;
using Ananke.A2A.Client;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using AgentToAgentProtocolDemo;

using AgentMessage = Ananke.Abstractions.Agents.AgentMessage;

// ---------------------------------------------------------------------
//  Ananke - Agent-to-Agent Protocol Demo
//
//  Demonstrates the A2A protocol with Ananke in two modes:
//
//    dotnet run                    - runs server + client in one process
//    dotnet run -- --server        - runs the A2A server only (port 5120)
//    dotnet run -- --client        - runs the A2A client only (connects to localhost:5120)
//
//  The server exposes an Ananke workflow and tools as an A2A-compliant
//  agent. The client discovers the agent, inspects its skills, and
//  sends messages - demonstrating cross-agent communication.
//
//  Cross-language interop (no Ananke SDK required):
//
//    dotnet run -- --server
//    python python_client/a2a_client.py           (Python 3.9+)
//
// ---------------------------------------------------------------------

Console.OutputEncoding = Encoding.UTF8;

var mode = args.Length > 0 ? args[0].TrimStart('-').ToLowerInvariant() : "both";
var serverUrl = "http://localhost:5120";
var agentPath = "/a2a";

switch (mode)
{
    case "server":
        await RunServerAsync(serverUrl, agentPath);
        break;

    case "client":
        await RunClientAsync(serverUrl, agentPath);
        break;

    default:
        // Run both: start the server in background, then run the client
        await RunBothAsync(serverUrl, agentPath);
        break;
}

// ---------------------------------------------------------------------
//  Server
// ---------------------------------------------------------------------

async Task RunServerAsync(string url, string path, CancellationToken ct = default)
{
    PrintBanner("A2A Server");
    Console.WriteLine($"  Endpoint: {url}{path}");
    Console.WriteLine($"  Card:     {url}/.well-known/agent-card.json");
    Console.WriteLine();

    var app = BuildServer(url, path);
    await app.RunAsync(ct);
}

WebApplication BuildServer(string url, string path)
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.WebHost.UseUrls(url);

    var app = builder.Build();

    var taskManager = new TaskManager();
    EchoAgent.Attach(taskManager, $"{url}{path}");

    app.MapA2AEndpoint(path, taskManager);
    return app;
}

// ---------------------------------------------------------------------
//  Client
// ---------------------------------------------------------------------

async Task RunClientAsync(string url, string path)
{
    PrintBanner("A2A Client");
    Console.WriteLine($"  Target: {url}{path}");
    Console.WriteLine();

    // -- 1. Discover the agent ----------------------------------------

    Console.WriteLine("-- Step 1: Agent Discovery --");
    Console.WriteLine();

    var discovery = new A2AAgentDiscovery();
    var info = await discovery.DiscoverAsync(new Uri(url));

    Console.WriteLine($"  Name:        {info.Name}");
    Console.WriteLine($"  Description: {info.Description}");
    Console.WriteLine($"  Version:     {info.Version}");
    Console.WriteLine($"  Streaming:   {info.SupportsStreaming}");
    Console.WriteLine($"  Skills:      {info.Skills.Count}");
    foreach (var skill in info.Skills)
        Console.WriteLine($"    - {skill.Name} - {skill.Description}");
    Console.WriteLine();

    // -- 2. Use A2AAgentModel (IStreamingAgentModel) ------------------

    Console.WriteLine("-- Step 2: Send messages via A2AAgentModel --");
    Console.WriteLine();

    var agentModel = new A2AAgentModel(new A2AAgentModelOptions
    {
        AgentUrl = new Uri($"{url}{path}")
    });

    // 2a - Pipeline execution (plain text - runs the workflow)
    Console.WriteLine("  [Pipeline] Sending: \"Hello from the A2A client!\"");
    var response = await agentModel.GenerateAsync(new AgentRequest
    {
        Messages = [AgentMessage.User("Hello from the A2A client!")]
    });
    Console.WriteLine($"  [Pipeline] Response: {response.Text}");
    Console.WriteLine();

    // 2b - Tool dispatch (command: argument format)
    Console.WriteLine("  [Tool] Sending: \"word_count: The quick brown fox jumps over the lazy dog\"");
    response = await agentModel.GenerateAsync(new AgentRequest
    {
        Messages = [AgentMessage.User("word_count: The quick brown fox jumps over the lazy dog")]
    });
    Console.WriteLine($"  [Tool] Response: {response.Text}");
    Console.WriteLine();

    Console.WriteLine("  [Tool] Sending: \"reverse: Ananke\"");
    response = await agentModel.GenerateAsync(new AgentRequest
    {
        Messages = [AgentMessage.User("reverse: Ananke")]
    });
    Console.WriteLine($"  [Tool] Response: {response.Text}");
    Console.WriteLine();

    Console.WriteLine("  [Tool] Sending: \"uppercase: distributed state machines\"");
    response = await agentModel.GenerateAsync(new AgentRequest
    {
        Messages = [AgentMessage.User("uppercase: distributed state machines")]
    });
    Console.WriteLine($"  [Tool] Response: {response.Text}");
    Console.WriteLine();

    // -- 3. Show interoperability -------------------------------------

    Console.WriteLine("-- Step 3: Interoperability - A2A agent as IAgentModel --");
    Console.WriteLine();
    Console.WriteLine("  The A2AAgentModel implements IStreamingAgentModel, so it");
    Console.WriteLine("  plugs into any Ananke workflow, AgentJob, or model router");
    Console.WriteLine("  exactly like a local OpenAI/Anthropic/Google model.");
    Console.WriteLine();

    // 3a - Use in a direct request with system prompt
    Console.WriteLine("  [WithSystemPrompt] Sending request with system context...");
    response = await agentModel.GenerateAsync(new AgentRequest
    {
        SystemPrompt = "Process all input through the text pipeline.",
        Messages = [AgentMessage.User("Ananke framework")]
    });
    Console.WriteLine($"  [WithSystemPrompt] Response: {response.Text}");
    Console.WriteLine();
}

// ---------------------------------------------------------------------
//  Both (default)
// ---------------------------------------------------------------------

async Task RunBothAsync(string url, string path)
{
    PrintBanner("A2A Demo (Server + Client)");
    Console.WriteLine();

    // Start the server in the background
    Console.WriteLine("  Starting A2A server...");
    var serverApp = BuildServer(url, path);
    await serverApp.StartAsync();
    Console.WriteLine($"  - Server listening at {url}{path}");
    Console.WriteLine($"  - Agent card at {url}/.well-known/agent-card.json");
    Console.WriteLine();

    try
    {
        // Run the client against the in-process server
        await RunClientAsync(url, path);
    }
    finally
    {
        await serverApp.StopAsync();
    }

    Console.WriteLine("----------------------------------------------------------");
    Console.WriteLine("  Done.");
    Console.WriteLine("----------------------------------------------------------");
}

// -- Helpers ----------------------------------------------------------

void PrintBanner(string title)
{
    Console.WriteLine("----------------------------------------------------------");
    Console.WriteLine($"  Ananke - {title}");
    Console.WriteLine("----------------------------------------------------------");
    Console.WriteLine();
}

using Ananke.OpenTelemetry;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;

internal static class ChatEndpoint
{
    // Registers POST /api/chat on the WebApplication as a Server-Sent Events (SSE) stream.
    internal static void MapChatEndpoint(
        this WebApplication app,
        IStreamingAgentModel agentModel,
        ToolKit stockTools,
        TracingPipeline? tracing)
    {
        app.MapPost("/api/chat", async (ChatRequest request, HttpContext context, CancellationToken ct) =>
            await AgenticApplication.HandleChat(request, context, agentModel, stockTools, tracing, ct))
           .WithName("Chat")
           .WithDescription("Send a message and receive a streaming response via SSE.")
           .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
    }
}

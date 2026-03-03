using Ananke.OpenTelemetry;
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using System.Text.Json;

internal static class AgenticApplication
{
    // Main handler: configures SSE, wires callbacks, and runs the streaming chat workflow.
    public static async Task HandleChat(
        ChatRequest request,
        HttpContext context,
        IStreamingAgentModel agentModel,
        ToolKit stockTools,
        TracingPipeline? tracing,
        CancellationToken ct)
    {
        // SSE requires these headers so the browser keeps the connection open.
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        var httpResponse = context.Response;
        var messages = BuildHistory(request);

        var workflow = StreamingChatWorkflow.Create("agentic-chat", agentModel)
            .WithSystemPrompt(AgentConfig.SystemPrompt)
            .WithTools(stockTools)
            .WithMaxToolRounds(10)
            .OnTextDelta(async delta => await WriteSse(httpResponse, "delta", new { text = delta }))
            .OnToolResult(async (name, result) => await WriteSse(httpResponse, "tool", new { name, result }))
            .Build();

        if (tracing is not null)
            workflow.UseTracing(tracing.Tracer);

        var execution = await workflow.RunAsync(new StreamingChatState { Messages = messages }, ct);

        // Send the terminal SSE event based on execution outcome.
        var finalState = execution.State;
        if (execution.Status == ExecutionStatus.Completed && finalState.LastResponse?.RequiresAction == true)
            await WriteSse(httpResponse, "error", new { message = "Tool-calling limit reached" });
        else if (execution.Status == ExecutionStatus.Completed)
            await WriteSse(httpResponse, "done", new { text = finalState.FullText });
        else if (execution.Status == ExecutionStatus.Faulted)
            await WriteSse(httpResponse, "error", new { message = execution.Result?.Error ?? "Workflow failed" });
    }

    // Converts the request history into AgentMessages and appends the new user message.
    private static List<AgentMessage> BuildHistory(ChatRequest request)
    {
        var messages = new List<AgentMessage>();
        foreach (var msg in request.History ?? [])
        {
            messages.Add(msg.Role == "user"
                ? AgentMessage.User(msg.Content)
                : AgentMessage.Assistant(msg.Content));
        }
        messages.Add(AgentMessage.User(request.Message));
        return messages;
    }

    // Serialises data as JSON and writes it as an SSE event, then flushes so the browser receives it right away.
    private static async Task WriteSse(HttpResponse response, string eventName, object data)
    {
        var json = JsonSerializer.Serialize(data);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n");
        await response.Body.FlushAsync();
    }
}

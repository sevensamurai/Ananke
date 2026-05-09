using Ananke.AspNetCore.Sse;
using Ananke.OpenTelemetry;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Tools;

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
        context.Response.EnableSse();

        var httpResponse = context.Response;
        var messages = BuildHistory(request);

        var workflow = StreamingChatWorkflow.Create("agentic-chat", agentModel)
            .WithSystemPrompt(AgentConfig.SystemPrompt)
            .WithTools(stockTools)
            .WithMaxToolRounds(10)
            .OnTextDelta(async delta => await httpResponse.WriteSseAsync("delta", new { text = delta }))
            .OnToolResult(async (name, result) => await httpResponse.WriteSseAsync("tool", new { name, result }))
            .Build();

        if (tracing is not null)
            workflow.UseTracing(tracing.Tracer);

        var execution = await workflow.RunAsync(new StreamingChatState { Messages = messages }, ct);

        // Send the terminal SSE event based on execution outcome.
        var finalState = execution.State;
        if (execution.Status == ExecutionStatus.Completed && finalState.LastResponse?.RequiresAction == true)
            await httpResponse.WriteSseAsync("error", new { message = "Tool-calling limit reached" });
        else if (execution.Status == ExecutionStatus.Completed)
            await httpResponse.WriteSseAsync("done", new { text = finalState.FullText });
        else if (execution.Status == ExecutionStatus.Faulted)
            await httpResponse.WriteSseAsync("error", new { message = execution.Result?.Error ?? "Workflow failed" });
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
}

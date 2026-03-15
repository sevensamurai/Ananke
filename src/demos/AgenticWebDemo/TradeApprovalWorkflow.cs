using Ananke.AspNetCore.Sse;
using Ananke.OpenTelemetry;
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Tools;
using System.Text;
using System.Text.Json;

/// <summary>
/// Demonstrates a human-in-the-loop workflow using InterruptBefore.
/// The agent analyzes a trade, the workflow pauses for human approval,
/// then executes the trade once approved.
/// </summary>
internal static class TradeApprovalWorkflow
{
    // Shared checkpoint store so analyze and approve calls share state.
    private static readonly InMemoryCheckpointStore CheckpointStore = new();

    internal static async Task HandleAnalyze(
        TradeAnalysisRequest request,
        HttpContext context,
        IStreamingAgentModel agentModel,
        ToolKit stockTools,
        TracingPipeline? tracing,
        CancellationToken ct)
    {
        context.Response.EnableSse();
        var httpResponse = context.Response;

        var workflow = BuildWorkflow(agentModel, stockTools, httpResponse, tracing);

        var initialState = new TradeState { UserRequest = request.Message };
        var execution = await workflow.RunAsync(initialState, ct);

        if (execution.Status == ExecutionStatus.Interrupted)
        {
            await httpResponse.WriteSseAsync("interrupted", new
            {
                executionId = execution.Id,
                analysis = execution.State.Analysis,
                message = "Trade analysis complete. Approve or reject to continue."
            });
        }
        else if (execution.Status == ExecutionStatus.Completed)
        {
            await httpResponse.WriteSseAsync("done", new { text = execution.State.Result });
        }
        else if (execution.Status == ExecutionStatus.Faulted)
        {
            await httpResponse.WriteSseAsync("error", new { message = execution.Result?.Error ?? "Workflow failed" });
        }
    }

    internal static async Task HandleApproval(
        TradeApprovalRequest request,
        IStreamingAgentModel agentModel,
        ToolKit stockTools,
        TracingPipeline? tracing,
        HttpContext context,
        CancellationToken ct)
    {
        context.Response.EnableSse();
        var httpResponse = context.Response;

        // Rebuild the workflow with the fresh HTTP response for SSE streaming.
        var workflow = BuildWorkflow(agentModel, stockTools, httpResponse, tracing);

        WorkflowExecution<TradeState> execution;
        if (request.Approved)
        {
            execution = await workflow.ResumeAsync(
                request.ExecutionId,
                state => state with { Approved = true },
                ct);
        }
        else
        {
            execution = await workflow.ResumeAsync(
                request.ExecutionId,
                state => state with { Approved = false },
                ct);
        }

        if (execution.Status == ExecutionStatus.Completed)
        {
            await httpResponse.WriteSseAsync("done", new { text = execution.State.Result });
        }
        else if (execution.Status == ExecutionStatus.Faulted)
        {
            await httpResponse.WriteSseAsync("error", new { message = execution.Result?.Error ?? "Workflow failed" });
        }
    }

    /// <summary>
    /// Builds the trade-approval workflow. Called from both analyze (first run) and
    /// approve (resume). The <paramref name="httpResponse"/> is captured by job lambdas
    /// for SSE streaming, so it must be the current request's response.
    /// </summary>
    private static Workflow<TradeState> BuildWorkflow(
        IStreamingAgentModel agentModel,
        ToolKit stockTools,
        HttpResponse httpResponse,
        TracingPipeline? tracing)
    {
        var workflow = new Workflow<TradeState>("trade-approval")
            .Job("analyze", async (state, jobCt) =>
            {
                var messages = new List<AgentMessage> { AgentMessage.User(state.UserRequest) };
                var analyzeTools = stockTools.Tools.Values
                    .Where(t => t.Name is "get_stock_price" or "get_stock_fundamentals" or "get_market_news")
                    .Select(t => new AgentTool(t.Name, t.Description, t.ParametersJsonSchema))
                    .ToList();

                var fullText = new StringBuilder();

                // Tool-calling loop: stream → execute tools → re-stream until the model produces text.
                for (var round = 0; round < 5; round++)
                {
                    var agentRequest = new AgentRequest
                    {
                        SystemPrompt = """
                            You are a stock trading analyst. Analyze the user's trade request using the
                            available tools. Research the stock price, fundamentals, and news.
                            Then provide a clear recommendation: BUY or SELL, the symbol, the quantity,
                            and a brief rationale. Format your response with Markdown.
                            """,
                        Messages = messages,
                        Tools = analyzeTools
                    };

                    fullText.Clear();
                    AgentResponse? completed = null;

                    await foreach (var chunk in agentModel.GenerateStreamAsync(agentRequest, jobCt))
                    {
                        if (chunk.TextDelta is not null)
                        {
                            fullText.Append(chunk.TextDelta);
                            await httpResponse.WriteSseAsync("delta", new { text = chunk.TextDelta });
                        }
                        if (chunk.CompletedResponse is not null)
                            completed = chunk.CompletedResponse;
                    }

                    if (completed?.RequiresAction != true)
                        break;

                    // Execute tool calls and feed results back for the next round.
                    messages.Add(AgentMessage.Assistant(completed.Text ?? string.Empty, completed.ToolCalls));
                    foreach (var call in completed.ToolCalls!)
                    {
                        var args = ParseToolArgs(call.Arguments);
                        var toolResult = stockTools.Tools.TryGetValue(call.FunctionName, out var tool)
                            ? await tool.ExecuteAsync(args, jobCt)
                            : ToolResult.Error($"Unknown tool: {call.FunctionName}");
                        await httpResponse.WriteSseAsync("tool", new { name = call.FunctionName, result = toolResult.Value });
                        messages.Add(AgentMessage.ToolResult(call.Id, toolResult.Value));
                    }
                }

                return state with { Analysis = fullText.ToString() };
            })
            .Job("execute", async (state, jobCt) =>
            {
                if (!state.Approved)
                    return state with { Result = "Trade was rejected by the reviewer." };

                var messages = new List<AgentMessage>
                {
                    AgentMessage.User($"Approved trade analysis:\n{state.Analysis}\n\nExecute this trade now.")
                };
                var executeTools = stockTools.Tools.Values
                    .Where(t => t.Name is "buy_shares" or "sell_shares")
                    .Select(t => new AgentTool(t.Name, t.Description, t.ParametersJsonSchema))
                    .ToList();

                var executeText = new StringBuilder();

                // Tool-calling loop for trade execution.
                for (var round = 0; round < 3; round++)
                {
                    var agentRequest = new AgentRequest
                    {
                        SystemPrompt = """
                            You are a trade execution agent. The trade has been approved by a human reviewer.
                            Execute the trade using the buy_shares or sell_shares tool based on the analysis.
                            Confirm the result.
                            """,
                        Messages = messages,
                        Tools = executeTools
                    };

                    executeText.Clear();
                    AgentResponse? completed = null;

                    await foreach (var chunk in agentModel.GenerateStreamAsync(agentRequest, jobCt))
                    {
                        if (chunk.TextDelta is not null)
                        {
                            executeText.Append(chunk.TextDelta);
                            await httpResponse.WriteSseAsync("delta", new { text = chunk.TextDelta });
                        }
                        if (chunk.CompletedResponse is not null)
                            completed = chunk.CompletedResponse;
                    }

                    if (completed?.RequiresAction != true)
                        break;

                    messages.Add(AgentMessage.Assistant(completed.Text ?? string.Empty, completed.ToolCalls));
                    foreach (var call in completed.ToolCalls!)
                    {
                        var args = ParseToolArgs(call.Arguments);
                        var toolResult = stockTools.Tools.TryGetValue(call.FunctionName, out var tool)
                            ? await tool.ExecuteAsync(args, jobCt)
                            : ToolResult.Error($"Unknown tool: {call.FunctionName}");
                        await httpResponse.WriteSseAsync("tool", new { name = call.FunctionName, result = toolResult.Value });
                        messages.Add(AgentMessage.ToolResult(call.Id, toolResult.Value));
                    }
                }

                return state with { Result = executeText.ToString() };
            })
            .Then("analyze", "execute")
            .Then("execute", Workflow.End)
            .InterruptBefore("execute")
            .UseCheckpointing(CheckpointStore);

        if (tracing is not null)
            workflow.UseTracing(tracing.Tracer);

        return workflow;
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArgs(string arguments)
    {
        var dict = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(arguments);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }
}

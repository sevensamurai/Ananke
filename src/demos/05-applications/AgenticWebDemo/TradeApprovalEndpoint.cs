using Ananke.OpenTelemetry;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Tools;

internal static class TradeApprovalEndpoint
{
    /// <summary>
    /// Registers the trade-approval HITL endpoints:
    /// <list type="bullet">
    ///   <item>POST /api/trade/analyze — starts the analysis, streams SSE, returns interrupted with executionId</item>
    ///   <item>POST /api/trade/approve — resumes with human approval/rejection, streams execution result</item>
    /// </list>
    /// </summary>
    internal static void MapTradeApprovalEndpoints(
        this WebApplication app,
        IStreamingAgentModel agentModel,
        ToolKit stockTools,
        TracingPipeline? tracing)
    {
        app.MapPost("/api/trade/analyze", async (TradeAnalysisRequest request, HttpContext context, CancellationToken ct) =>
            await TradeApprovalWorkflow.HandleAnalyze(request, context, agentModel, stockTools, tracing, ct))
           .WithName("TradeAnalyze")
           .WithDescription("Analyze a trade request. Returns SSE stream ending with an 'interrupted' event containing the executionId.")
           .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        app.MapPost("/api/trade/approve", async (TradeApprovalRequest request, HttpContext context, CancellationToken ct) =>
            await TradeApprovalWorkflow.HandleApproval(request, agentModel, stockTools, tracing, context, ct))
           .WithName("TradeApprove")
           .WithDescription("Approve or reject a pending trade. Resumes the interrupted workflow.")
           .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
    }
}

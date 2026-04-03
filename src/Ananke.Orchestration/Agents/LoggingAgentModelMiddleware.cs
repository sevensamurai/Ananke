using Microsoft.Extensions.Logging;

using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Middleware that logs request and response metadata for each LLM call:
/// message count, tool count, response length, tool-call presence, and latency.
/// </summary>
/// <remarks>
/// <para>
/// Logs at <see cref="LogLevel.Information"/> for successful calls and
/// includes elapsed time. Does not log the actual prompt or response text
/// to avoid leaking sensitive content — only structural metadata.
/// </para>
/// <para>
/// Register via <see cref="MiddlewareAgentModel.Wrap(IStreamingAgentModel, IAgentModelMiddleware[])"/>:
/// </para>
/// <code>
/// var model = MiddlewareAgentModel.Wrap(innerModel,
///     new LoggingAgentModelMiddleware(loggerFactory));
/// </code>
/// </remarks>
public sealed class LoggingAgentModelMiddleware : IAgentModelMiddleware
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a logging middleware that writes to the specified logger factory.
    /// </summary>
    /// <param name="loggerFactory">Factory used to create the logger instance.</param>
    public LoggingAgentModelMiddleware(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger("Ananke.Orchestration.Agents.Middleware.Logging");
    }

    /// <summary>
    /// Creates a logging middleware that writes to the specified logger.
    /// </summary>
    /// <param name="logger">Logger to write to.</param>
    public LoggingAgentModelMiddleware(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AgentRequest> OnBeforeGenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "LLM request: messages={MessageCount}, tools={ToolCount}, hasSystemPrompt={HasSystemPrompt}",
            request.Messages.Count,
            request.Tools?.Count ?? 0,
            request.SystemPrompt is not null);

        return Task.FromResult(request);
    }

    /// <inheritdoc />
    public Task<AgentResponse> OnAfterGenerateAsync(
        AgentResponse response, AgentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "LLM response: textLength={TextLength}, hasToolCalls={HasToolCalls}, toolCallCount={ToolCallCount}",
            response.Text?.Length ?? 0,
            response.RequiresAction,
            response.ToolCalls?.Count ?? 0);

        return Task.FromResult(response);
    }
}

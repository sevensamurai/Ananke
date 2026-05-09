using System.Text.Json;
using A2A;

namespace AgentToAgentProtocolDemo;

/// <summary>
/// Minimal ASP.NET Core endpoint that bridges HTTP POST requests to an
/// A2A <see cref="TaskManager"/>. Handles the JSON-RPC dispatch for
/// <c>message/send</c> and <c>agent/authenticatedExtendedCard</c> methods.
/// </summary>
internal static class A2AEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Maps the A2A JSON-RPC endpoint at <paramref name="pattern"/>.
    /// </summary>
    internal static void MapA2AEndpoint(
        this WebApplication app,
        string pattern,
        TaskManager taskManager)
    {
        // Main JSON-RPC endpoint (message/send, agent card query, etc.)
        app.MapPost(pattern, async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var method = root.GetProperty("method").GetString();
            var id = root.TryGetProperty("id", out var idProp) ? idProp : default;

            switch (method)
            {
                case "message/send":
                    var msgParams = JsonSerializer.Deserialize<MessageSendParams>(
                        root.GetProperty("params").GetRawText(), A2AJsonUtilities.DefaultOptions)!;
                    var response = await taskManager.SendMessageAsync(msgParams, ctx.RequestAborted);
                    await WriteJsonRpcResponse(ctx, id, response!);
                    break;

                case "agent/authenticatedExtendedCard":
                    var agentUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}{pattern}";
                    if (taskManager.OnAgentCardQuery is not null)
                    {
                        var card = await taskManager.OnAgentCardQuery(agentUrl, ctx.RequestAborted);
                        await WriteJsonRpcResponse(ctx, id, card);
                    }
                    else
                    {
                        await WriteJsonRpcError(ctx, id, -32601, "Agent card not configured");
                    }
                    break;

                default:
                    await WriteJsonRpcError(ctx, id, -32601, $"Method not found: {method}");
                    break;
            }
        });

        // Well-known agent card endpoint (GET /.well-known/agent-card.json)
        var wellKnownPath = pattern.TrimEnd('/');
        app.MapGet("/.well-known/agent-card.json", async (HttpContext ctx) =>
        {
            var agentUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}{wellKnownPath}";
            if (taskManager.OnAgentCardQuery is not null)
            {
                var card = await taskManager.OnAgentCardQuery(agentUrl, ctx.RequestAborted);
                ctx.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(ctx.Response.Body, card, A2AJsonUtilities.DefaultOptions);
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
        });
    }

    private static async Task WriteJsonRpcResponse(HttpContext ctx, JsonElement id, object result)
    {
        ctx.Response.ContentType = "application/json";
        var wrapper = new
        {
            jsonrpc = "2.0",
            id = id.ValueKind != JsonValueKind.Undefined ? id.Deserialize<object>() : null,
            result
        };
        await JsonSerializer.SerializeAsync(ctx.Response.Body, wrapper, A2AJsonUtilities.DefaultOptions);
    }

    private static async Task WriteJsonRpcError(HttpContext ctx, JsonElement id, int code, string message)
    {
        ctx.Response.ContentType = "application/json";
        var wrapper = new
        {
            jsonrpc = "2.0",
            id = id.ValueKind != JsonValueKind.Undefined ? id.Deserialize<object>() : null,
            error = new { code, message }
        };
        await JsonSerializer.SerializeAsync(ctx.Response.Body, wrapper, A2AJsonUtilities.DefaultOptions);
    }
}

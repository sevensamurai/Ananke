// ToolExecutionMode enum moved to Ananke.Abstractions.Providers.ToolExecutionMode.
using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// Network endpoint metadata for remote-backed tools (<see cref="ToolExecutionMode.Callback"/>,
/// <see cref="ToolExecutionMode.Mcp"/>, or <see cref="ToolExecutionMode.OpenApi"/>).
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Uri"/> meaning varies by execution mode:
/// </para>
/// <list type="bullet">
///   <item><see cref="ToolExecutionMode.Callback"/> — HTTP endpoint the platform POSTs tool calls to.</item>
///   <item><see cref="ToolExecutionMode.Mcp"/> — MCP server URI (e.g. <c>http://localhost:3000/mcp</c>).</item>
///   <item><see cref="ToolExecutionMode.OpenApi"/> — URL of the OpenAPI specification document.</item>
/// </list>
/// </remarks>
public sealed record ToolEndpoint
{
    /// <summary>
    /// The endpoint URI. Interpretation depends on the <see cref="ToolExecutionMode"/>:
    /// callback URL, MCP server address, or OpenAPI spec location.
    /// </summary>
    public required Uri Uri { get; init; }

    /// <summary>
    /// Optional HTTP header name used for authentication (e.g. <c>"Authorization"</c>,
    /// <c>"X-API-Key"</c>). The header <b>value</b> is resolved at deploy time from
    /// credentials providers — never stored in the tool definition.
    /// </summary>
    public string? AuthHeader { get; init; }

    /// <summary>
    /// Optional query-string parameters appended to the endpoint URI at dispatch time.
    /// Use for key-based auth idioms that require a query parameter rather than a header
    /// (e.g. Azure Functions <c>?code=…</c>, API gateway routing keys).
    /// Values are resolved at deploy/dispatch time — never store secrets here directly;
    /// populate this dictionary from a credentials provider in the host.
    /// </summary>
    public IReadOnlyDictionary<string, string>? QueryParams { get; init; }
}

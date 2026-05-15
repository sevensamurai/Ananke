namespace Ananke.Orchestration.Tools;

/// <summary>
/// Describes how a tool's implementation is reached at runtime. Federation deployers
/// use this to determine whether a tool can be deployed to a remote platform.
/// </summary>
/// <remarks>
/// <para>
/// All <see cref="ToolKit"/> convenience overloads (<c>AddTool</c> with lambdas) default
/// to <see cref="Local"/>. Use the <see cref="ToolBuilder"/> fluent API to set other modes:
/// <c>.Callback(uri)</c>, <c>.Mcp(uri)</c>, <c>.OpenApi(uri)</c>, or <c>.PlatformNative(capability)</c>.
/// </para>
/// <para>
/// A tool can carry both a local <c>Execute</c> delegate <b>and</b> a remote endpoint.
/// The local delegate is used when running in-process or in hybrid mode; the endpoint
/// metadata tells a platform deployer where to point during deploy-to-platform.
/// </para>
/// </remarks>
public enum ToolExecutionMode
{
    /// <summary>
    /// In-process delegate (lambda, closure, method group). Runs in the Ananke host process.
    /// Cannot be deployed to a remote platform — suitable for local, hybrid, demos, and tests.
    /// </summary>
    Local,

    /// <summary>
    /// The platform calls a user-provided HTTP endpoint to execute the tool.
    /// Deployable when the endpoint is reachable from the platform.
    /// </summary>
    Callback,

    /// <summary>
    /// Backed by an MCP (Model Context Protocol) server. The platform or Ananke connects
    /// to the server to execute the tool. Deployable when the server is network-reachable.
    /// </summary>
    Mcp,

    /// <summary>
    /// The platform reads an OpenAPI specification and calls the described API directly.
    /// No user callback needed — the platform handles invocation. Deployable when the
    /// spec URL and API are reachable.
    /// </summary>
    OpenApi,

    /// <summary>
    /// A platform-native capability (e.g. Vertex AI Code Interpreter, Claude web_search).
    /// No user code or endpoint — the platform handles execution internally.
    /// Always deployable; may be unavailable when running locally.
    /// </summary>
    PlatformNative
}

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

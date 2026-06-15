namespace Ananke.Abstractions.Providers;

/// <summary>
/// Describes how a tool's implementation is reached at runtime. Federation deployers
/// use this to determine whether a tool can be deployed to a remote platform.
/// </summary>
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

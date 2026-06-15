using System.Diagnostics;
using System.Text.Json;
using Ananke.Abstractions;
using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// Represents the outcome of a tool execution — either a successful value or an error message.
/// Both cases carry a string that is sent to the LLM as the tool result.
/// The framework uses <see cref="IsError"/> to branch on observability (logging, span status)
/// without changing the message flow.
/// </summary>
public readonly record struct ToolResult(string Value, bool IsError)
{
    private static readonly JsonSerializerOptions JsonOptions = AnankeJson.Display;

    /// <summary>
    /// Whether this error is transient and the tool call could succeed on retry.
    /// Defaults to <c>true</c>. Set to <c>false</c> for permanent failures
    /// (e.g. usage/argument errors) that would fail identically on retry.
    /// </summary>
    public bool IsRetryable { get; init; } = true;

    public static ToolResult Ok(string value) => new(value, IsError: false);
    public static ToolResult Error(string error) => new(error, IsError: true);

    /// <summary>
    /// Creates a non-retryable error — signals the agent to stop calling this tool
    /// because the failure is permanent (bad configuration, unknown arguments, etc.).
    /// </summary>
    public static ToolResult Fatal(string error) => new(error, IsError: true) { IsRetryable = false };

    /// <summary>
    /// Serializes <paramref name="value"/> to JSON and wraps it as a successful result.
    /// Use this to return structured data from tools without manual string formatting.
    /// </summary>
    public static ToolResult Json<T>(T value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), IsError: false);

    public static implicit operator ToolResult(string value) => Ok(value);
}

/// <summary>
/// A runtime dependency that a tool requires to function (e.g. a CLI binary on PATH).
/// Checked eagerly by <see cref="ToolKit.CheckPrerequisitesAsync"/> at startup,
/// before any tool is exposed to an agent.
/// </summary>
/// <param name="Name">Short identifier (e.g. <c>"uvx"</c>, <c>"node"</c>, <c>"docker"</c>).</param>
/// <param name="Check">Returns <see langword="true"/> when the prerequisite is satisfied.</param>
/// <param name="InstallHint">
/// Human-readable install instruction shown when the check fails.
/// Keep it actionable — a single command or a short URL.
/// </param>
public sealed record ToolPrerequisite(string Name, Func<CancellationToken, Task<bool>> Check, string InstallHint)
{
    /// <summary>
    /// Creates a prerequisite that verifies a CLI binary is reachable on <c>PATH</c>.
    /// The check runs <c>&lt;binary&gt; --version</c> and expects a zero exit code.
    /// </summary>
    /// <param name="binary">The executable name (e.g. <c>"uvx"</c>, <c>"node"</c>).</param>
    /// <param name="installHint">
    /// Shown when the binary is missing.
    /// Example: <c>"Install uv: winget install astral-sh.uv — see docs/guides/uv-setup-for-dotnet-developers.md"</c>
    /// </param>
    /// <param name="versionFlag">The flag used to probe the binary. Defaults to <c>"--version"</c>.</param>
    public static ToolPrerequisite Binary(string binary, string installHint, string versionFlag = "--version") =>
        new(binary, async ct =>
        {
            try
            {
                var psi = new ProcessStartInfo(binary, versionFlag)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process is null) return false;
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                return process.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }, installHint);

    /// <summary>
    /// Creates a prerequisite that verifies a network endpoint is reachable via HTTP HEAD.
    /// Use for <see cref="ToolExecutionMode.Callback"/>, <see cref="ToolExecutionMode.Mcp"/>,
    /// and <see cref="ToolExecutionMode.OpenApi"/> tools to fail fast at startup when the
    /// remote service is unavailable.
    /// </summary>
    /// <param name="uri">The endpoint URI to probe.</param>
    /// <param name="installHint">
    /// Shown when the endpoint is unreachable.
    /// Example: <c>"Start the MCP server: dotnet run --project McpServer"</c>
    /// </param>
    /// <param name="timeout">HTTP request timeout. Defaults to 5 seconds.</param>
    public static ToolPrerequisite Endpoint(Uri uri, string installHint, TimeSpan? timeout = null) =>
        new(uri.Authority, async ct =>
        {
            try
            {
                using var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
                using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                // Any response (even 4xx/5xx) means the endpoint is reachable.
                // We're checking connectivity, not correctness.
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }, installHint);
}

/// <summary>
/// Describes a single parameter accepted by a <see cref="ToolDefinition"/>.
/// </summary>
/// <param name="Name">Parameter name (used as the JSON property key).</param>
/// <param name="Description">Human-readable description sent to the LLM.</param>
/// <param name="JsonType">JSON Schema type (e.g. <c>"string"</c>, <c>"integer"</c>, <c>"number"</c>, <c>"boolean"</c>).</param>
/// <param name="Examples">
/// Sample values for this parameter. Emitted as the JSON Schema <c>examples</c> annotation,
/// which helps the LLM produce correct values — especially for ambiguous, format-sensitive,
/// or enum-like parameters.
/// </param>
/// <param name="IsRequired">When <c>true</c>, the parameter is included in the JSON Schema <c>required</c> array.</param>
public record ToolParameter(
    string Name,
    string Description,
    string JsonType = "string",
    IReadOnlyList<string>? Examples = null,
    bool IsRequired = false);

public record ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<ToolParameter> Parameters { get; init; }

    /// <summary>
    /// How the tool's implementation is reached at runtime. Defaults to
    /// <see cref="ToolExecutionMode.Local"/> (in-process delegate).
    /// Federation deployers inspect this to determine deployability.
    /// </summary>
    public ToolExecutionMode ExecutionMode { get; init; } = ToolExecutionMode.Local;

    /// <summary>
    /// Network endpoint for remote-backed tools (<see cref="ToolExecutionMode.Callback"/>,
    /// <see cref="ToolExecutionMode.Mcp"/>, <see cref="ToolExecutionMode.OpenApi"/>).
    /// <see langword="null"/> for <see cref="ToolExecutionMode.Local"/> and
    /// <see cref="ToolExecutionMode.PlatformNative"/> tools.
    /// </summary>
    public ToolEndpoint? Endpoint { get; init; }

    /// <summary>
    /// Platform-native capability identifier (e.g. <c>"code_execution"</c>,
    /// <c>"web_search"</c>, <c>"vertex_extension:code_interpreter"</c>).
    /// Only meaningful when <see cref="ExecutionMode"/> is
    /// <see cref="ToolExecutionMode.PlatformNative"/>.
    /// </summary>
    public string? PlatformCapability { get; init; }

    /// <summary>
    /// Keywords for categorisation, filtering, and discovery.
    /// Used by <c>AgentCardBuilder</c> when mapping tools to A2A skills.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Sample invocations or usage descriptions. Included in the tool description
    /// sent to the LLM to improve tool-calling accuracy.
    /// </summary>
    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>
    /// Runtime dependencies this tool needs (e.g. CLI binaries on PATH).
    /// Validated at startup by <see cref="ToolKit.CheckPrerequisitesAsync"/>.
    /// Tools with no external dependencies leave this empty.
    /// </summary>
    public IReadOnlyList<ToolPrerequisite> Requires { get; init; } = [];

    public required Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> Execute { get; init; }

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default) =>
        Execute(args, ct);

    /// <summary>
    /// Projects this definition into a schema-only <see cref="ProviderTool"/> for use with
    /// <see cref="IToolSchemaTranslator"/>. Execution delegate and prerequisites are dropped.
    /// </summary>
    public ProviderTool ToProviderTool() =>
        new(Name, Description, ParametersJsonSchema)
        {
            ExecutionMode = ExecutionMode,
            PlatformCapability = PlatformCapability
        };

    public string ParametersJsonSchema
    {
        get
        {
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var param in Parameters)
            {
                var prop = new Dictionary<string, object>
                {
                    ["type"] = param.JsonType,
                    ["description"] = param.Description
                };

                if (param.Examples is { Count: > 0 })
                    prop["examples"] = param.Examples;

                properties[param.Name] = prop;
                if (param.IsRequired)
                    required.Add(param.Name);
            }

            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            });
        }
    }
}

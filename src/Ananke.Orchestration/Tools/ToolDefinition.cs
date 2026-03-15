using System.Text.Json;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// Represents the outcome of a tool execution — either a successful value or an error message.
/// Both cases carry a string that is sent to the LLM as the tool result.
/// The framework uses <see cref="IsError"/> to branch on observability (logging, span status)
/// without changing the message flow.
/// </summary>
public readonly record struct ToolResult(string Value, bool IsError)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ToolResult Ok(string value) => new(value, IsError: false);
    public static ToolResult Error(string error) => new(error, IsError: true);

    /// <summary>
    /// Serializes <paramref name="value"/> to JSON and wraps it as a successful result.
    /// Use this to return structured data from tools without manual string formatting.
    /// </summary>
    public static ToolResult Json<T>(T value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), IsError: false);

    public static implicit operator ToolResult(string value) => Ok(value);
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
public record ToolParameter(
    string Name,
    string Description,
    string JsonType = "string",
    IReadOnlyList<string>? Examples = null);

public record ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<ToolParameter> Parameters { get; init; }

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

    public required Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> Execute { get; init; }

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default) =>
        Execute(args, ct);

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

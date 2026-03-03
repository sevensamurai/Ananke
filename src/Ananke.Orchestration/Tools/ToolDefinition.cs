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
    public static ToolResult Ok(string value) => new(value, IsError: false);
    public static ToolResult Error(string error) => new(error, IsError: true);
    public static implicit operator ToolResult(string value) => Ok(value);
}

public record ToolParameter(string Name, string Description, string JsonType = "string");

public record ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<ToolParameter> Parameters { get; init; }

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
                properties[param.Name] = new Dictionary<string, string>
                {
                    ["type"] = param.JsonType,
                    ["description"] = param.Description
                };
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

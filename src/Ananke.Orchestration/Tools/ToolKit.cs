using System.Globalization;
using System.Text.Json;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// A named collection of <see cref="ToolDefinition"/> instances made available to an
/// <c>AgentJob</c> for tool-calling workflows. Build a kit once and share it across agents.
/// </summary>
public sealed class ToolKit
{
    private readonly Dictionary<string, ToolDefinition> _tools = [];

    public string Name { get; }
    public IReadOnlyDictionary<string, ToolDefinition> Tools => _tools;

    public ToolKit(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public ToolKit AddTool(
        string name,
        string description,
        Func<ToolResult> execute)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [],
            Execute = (_, _) => Task.FromResult(execute())
        };
        return this;
    }

    public ToolKit AddTool(
        string name,
        string description,
        Func<Task<ToolResult>> execute)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [],
            Execute = (_, _) => execute()
        };
        return this;
    }

    public ToolKit AddTool(
        string name,
        string description,
        Func<string, ToolResult> execute,
        string paramName,
        string paramDescription)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription)],
            Execute = (args, _) =>
            {
                var arg = GetArg(args, paramName);
                return Task.FromResult(execute(arg));
            }
        };
        return this;
    }

    public ToolKit AddTool(
        string name,
        string description,
        Func<string, Task<ToolResult>> execute,
        string paramName,
        string paramDescription)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription)],
            Execute = (args, _) =>
            {
                var arg = GetArg(args, paramName);
                return execute(arg);
            }
        };
        return this;
    }

    public ToolKit AddTool(
        string name,
        string description,
        Func<string, string, ToolResult> execute,
        (string name, string description) param1,
        (string name, string description) param2)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(param1.name, param1.description), new(param2.name, param2.description)],
            Execute = (args, _) =>
            {
                var arg1 = GetArg(args, param1.name);
                var arg2 = GetArg(args, param2.name);
                return Task.FromResult(execute(arg1, arg2));
            }
        };
        return this;
    }

    public ToolKit AddTool(
        string name,
        string description,
        Func<string, string, Task<ToolResult>> execute,
        (string name, string description) param1,
        (string name, string description) param2)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(param1.name, param1.description), new(param2.name, param2.description)],
            Execute = (args, _) =>
            {
                var arg1 = GetArg(args, param1.name);
                var arg2 = GetArg(args, param2.name);
                return execute(arg1, arg2);
            }
        };
        return this;
    }

    public ToolKit AddTool<T>(
        string name,
        string description,
        Func<T, ToolResult> execute,
        string paramName,
        string paramDescription)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, JsonTypeFor(typeof(T)))],
            Execute = (args, _) =>
            {
                var arg = GetArg<T>(args, paramName);
                return Task.FromResult(execute(arg));
            }
        };
        return this;
    }

    public ToolKit AddTool<T>(
        string name,
        string description,
        Func<T, Task<ToolResult>> execute,
        string paramName,
        string paramDescription)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, JsonTypeFor(typeof(T)))],
            Execute = (args, _) =>
            {
                var arg = GetArg<T>(args, paramName);
                return execute(arg);
            }
        };
        return this;
    }

    public ToolKit AddTool<T1, T2>(
        string name,
        string description,
        Func<T1, T2, ToolResult> execute,
        (string name, string description) param1,
        (string name, string description) param2)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [
                new(param1.name, param1.description, JsonTypeFor(typeof(T1))),
                new(param2.name, param2.description, JsonTypeFor(typeof(T2)))
            ],
            Execute = (args, _) =>
            {
                var arg1 = GetArg<T1>(args, param1.name);
                var arg2 = GetArg<T2>(args, param2.name);
                return Task.FromResult(execute(arg1, arg2));
            }
        };
        return this;
    }

    public ToolKit AddTool<T1, T2>(
        string name,
        string description,
        Func<T1, T2, Task<ToolResult>> execute,
        (string name, string description) param1,
        (string name, string description) param2)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [
                new(param1.name, param1.description, JsonTypeFor(typeof(T1))),
                new(param2.name, param2.description, JsonTypeFor(typeof(T2)))
            ],
            Execute = (args, _) =>
            {
                var arg1 = GetArg<T1>(args, param1.name);
                var arg2 = GetArg<T2>(args, param2.name);
                return execute(arg1, arg2);
            }
        };
        return this;
    }

    private static string GetArg(IReadOnlyDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
            throw new ArgumentException($"Missing required tool argument: {name}");

        if (value is JsonElement element)
            return element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.GetRawText();

        return value.ToString() ?? string.Empty;
    }

    private static T GetArg<T>(IReadOnlyDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
            throw new ArgumentException($"Missing required tool argument: {name}");

        if (value is JsonElement element)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(element)
                    ?? throw new ArgumentException(
                        $"Tool argument '{name}' deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new ArgumentException(
                    $"Tool argument '{name}' could not be deserialized to {typeof(T).Name}. JSON: {element.GetRawText()}", ex);
            }
        }

        if (value is T typed)
            return typed;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                $"Tool argument '{name}' could not be converted to {typeof(T).Name}. Value: {value}");
        }
    }

    private static string JsonTypeFor(Type type) => type switch
    {
        _ when type == typeof(int) || type == typeof(long) => "integer",
        _ when type == typeof(float) || type == typeof(double) || type == typeof(decimal) => "number",
        _ when type == typeof(bool) => "boolean",
        _ => "string"
    };

    /// <summary>
    /// Copies all tools from <paramref name="other"/> into this kit.
    /// If both kits contain a tool with the same name, the tool from <paramref name="other"/> wins.
    /// </summary>
    /// <returns>This <see cref="ToolKit"/> for fluent chaining.</returns>
    public ToolKit Merge(ToolKit other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var (name, tool) in other.Tools)
            _tools[name] = tool;

        return this;
    }

    /// <summary>
    /// Registers a pre-built <see cref="ToolDefinition"/> directly.
    /// Use this when the tool is created externally (e.g. bridged from an MCP server).
    /// </summary>
    /// <returns>This <see cref="ToolKit"/> for fluent chaining.</returns>
    public ToolKit AddTool(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Name] = tool;
        return this;
    }
}

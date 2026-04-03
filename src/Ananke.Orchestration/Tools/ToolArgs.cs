using System.Globalization;
using System.Text.Json;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// Provides typed access to tool arguments passed by the LLM.
/// Wraps the raw <c>IReadOnlyDictionary&lt;string, object?&gt;</c> and handles
/// <see cref="JsonElement"/> deserialization and type conversion transparently.
/// </summary>
public sealed class ToolArgs
{
    private readonly IReadOnlyDictionary<string, object?> _args;

    internal ToolArgs(IReadOnlyDictionary<string, object?> args) => _args = args;

    /// <summary>
    /// Gets a string argument by name. <see cref="JsonElement"/> values are
    /// extracted as their string representation.
    /// </summary>
    /// <exception cref="ArgumentException">The argument is missing or <see langword="null"/>.</exception>
    public string Get(string name)
    {
        if (!_args.TryGetValue(name, out var value) || value is null)
            throw new ArgumentException($"Missing required tool argument: {name}");

        if (value is JsonElement element)
            return element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.GetRawText();

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Gets a typed argument by name. <see cref="JsonElement"/> values are deserialized
    /// via <see cref="JsonSerializer"/>; other values are cast or converted via
    /// <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The argument is missing, <see langword="null"/>, or cannot be converted to <typeparamref name="T"/>.
    /// </exception>
    public T Get<T>(string name)
    {
        if (!_args.TryGetValue(name, out var value) || value is null)
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

    /// <summary>
    /// Maps a CLR type to its JSON Schema type name.
    /// </summary>
    internal static string JsonTypeFor(Type type) => type switch
    {
        _ when type == typeof(int) || type == typeof(long) => "integer",
        _ when type == typeof(float) || type == typeof(double) || type == typeof(decimal) => "number",
        _ when type == typeof(bool) => "boolean",
        _ => "string"
    };
}

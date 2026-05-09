using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ananke.Tool.Shared;

/// <summary>
/// Writes structured JSON to stdout when <c>--json</c> is active.
/// Consistent envelope format across all commands.
/// </summary>
public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serializes <paramref name="value"/> as indented camelCase JSON to stdout.
    /// </summary>
    public static void Write<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, Options));
}

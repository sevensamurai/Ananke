using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ananke.Tool.Output;

/// <summary>
/// Writes structured JSON to stdout when <c>--json</c> is active.
/// All commands delegate their output through this class so the JSON
/// envelope format is consistent.
/// </summary>
internal static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serializes <paramref name="value"/> as indented JSON to stdout.
    /// </summary>
    public static void Write<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, Options));
}

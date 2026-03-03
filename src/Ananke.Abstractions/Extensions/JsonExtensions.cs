using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ananke.Abstractions.Extensions;

public static class JsonExtensions
{
    internal readonly static JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(this object? item)
    {
        if (item is null)
            return string.Empty;
        return JsonSerializer.Serialize(item, _jsonOptions);
    }
}

using System.Text.Json;
using Google.GenAI.Types;
using GType = Google.GenAI.Types.Type;

namespace Ananke.Orchestration.Google;

/// <summary>
/// Converts a standard JSON Schema string into Google GenAI's <see cref="Schema"/> type tree.
/// Handles the common subset used by Ananke: object, array, string, number, integer, boolean, and enum.
/// </summary>
internal static class JsonSchemaConverter
{
    /// <summary>
    /// Parses a JSON Schema string and converts it to a Google <see cref="Schema"/>.
    /// </summary>
    public static Schema Convert(string jsonSchema)
    {
        using var doc = JsonDocument.Parse(jsonSchema);
        return ConvertElement(doc.RootElement);
    }

    private static Schema ConvertElement(JsonElement element)
    {
        var schema = new Schema();

        if (element.TryGetProperty("type", out var typeProp))
        {
            schema.Type = typeProp.GetString() switch
            {
                "string" => GType.String,
                "number" => GType.Number,
                "integer" => GType.Integer,
                "boolean" => GType.Boolean,
                "array" => GType.Array,
                "object" => GType.Object,
                _ => GType.String
            };
        }

        if (element.TryGetProperty("description", out var descProp))
            schema.Description = descProp.GetString();

        if (element.TryGetProperty("title", out var titleProp))
            schema.Title = titleProp.GetString();

        if (element.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
        {
            schema.Enum = enumProp.EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();
        }

        if (element.TryGetProperty("properties", out var propsProp) && propsProp.ValueKind == JsonValueKind.Object)
        {
            schema.Properties = new Dictionary<string, Schema>();
            foreach (var prop in propsProp.EnumerateObject())
                schema.Properties[prop.Name] = ConvertElement(prop.Value);
        }

        if (element.TryGetProperty("required", out var reqProp) && reqProp.ValueKind == JsonValueKind.Array)
        {
            schema.Required = reqProp.EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();
        }

        if (element.TryGetProperty("items", out var itemsProp))
            schema.Items = ConvertElement(itemsProp);

        return schema;
    }
}

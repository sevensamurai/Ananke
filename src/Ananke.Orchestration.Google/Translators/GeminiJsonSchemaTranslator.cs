using System.Text.Json;
using Ananke.Orchestration.Translators;
using Google.GenAI.Types;

namespace Ananke.Orchestration.Google.Translators;

/// <summary>
/// Translates standard Ananke JSON Schema dictionaries to Google GenAI
/// <see cref="Schema"/> objects using the existing <see cref="JsonSchemaConverter"/>.
/// </summary>
public sealed class GeminiJsonSchemaTranslator : IJsonSchemaTranslator
{
    /// <inheritdoc />
    /// <returns>A Google GenAI <see cref="Schema"/> object.</returns>
    public object Translate(IReadOnlyDictionary<string, object> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var json = JsonSerializer.Serialize(schema);
        return JsonSchemaConverter.Convert(json);
    }
}

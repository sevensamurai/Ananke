using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ananke.Orchestration.Agents;

public static class JsonSchemaGenerator
{
    private const int MaxDepth = 10;

    public static string Generate<T>() =>
        JsonSerializer.Serialize(GenerateForType(typeof(T)));

    public static Dictionary<string, object> GenerateForType(Type type) =>
        GenerateForType(type, depth: 0);

    private static Dictionary<string, object> GenerateForType(Type type, int depth)
    {
        if (depth > MaxDepth)
            return new Dictionary<string, object> { ["type"] = "object" };

        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var prop in type.GetProperties().Where(p => p.CanRead))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                continue;

            var propName = GetJsonPropertyName(prop);
            properties[propName] = GetPropertySchema(prop.PropertyType, depth + 1);
            required.Add(propName);
        }

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
    }

    private static string GetJsonPropertyName(PropertyInfo prop)
    {
        var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? prop.Name;
    }

    internal static Dictionary<string, object> GetPropertySchema(Type type) =>
        GetPropertySchema(type, depth: 0);

    private static Dictionary<string, object> GetPropertySchema(Type type, int depth)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var isNullable = underlying is not null;
        var actual = underlying ?? type;

        // Primitives
        if (actual == typeof(string))
            return Typed("string", isNullable);

        if (actual == typeof(int) || actual == typeof(long) ||
            actual == typeof(short) || actual == typeof(byte))
            return Typed("integer", isNullable);

        if (actual == typeof(double) || actual == typeof(float) || actual == typeof(decimal))
            return Typed("number", isNullable);

        if (actual == typeof(bool))
            return Typed("boolean", isNullable);

        // Date / time
        if (actual == typeof(DateTime) || actual == typeof(DateTimeOffset) || actual == typeof(DateOnly))
            return TypedWithFormat("string", "date-time", isNullable);

        if (actual == typeof(TimeSpan))
            return TypedWithFormat("string", "duration", isNullable);

        // Enum
        if (actual.IsEnum)
        {
            var names = Enum.GetNames(actual).Cast<object>().ToArray();
            return new Dictionary<string, object>
            {
                ["type"] = isNullable ? new[] { "string", "null" } : (object)"string",
                ["enum"] = names
            };
        }

        // Dictionary<TKey, TValue> → additionalProperties
        var dictValueType = GetDictionaryValueType(actual);
        if (dictValueType is not null)
        {
            return new Dictionary<string, object>
            {
                ["type"] = isNullable ? new object[] { "object", "null" } : (object)"object",
                ["additionalProperties"] = GetPropertySchema(dictValueType, depth + 1)
            };
        }

        // Array / collection
        var elementType = GetCollectionElementType(actual);
        if (elementType is not null)
        {
            return new Dictionary<string, object>
            {
                ["type"] = isNullable ? new object[] { "array", "null" } : (object)"array",
                ["items"] = GetPropertySchema(elementType, depth + 1)
            };
        }

        // Depth guard for nested objects
        if (depth >= MaxDepth)
            return new Dictionary<string, object> { ["type"] = "object" };

        // Nested object (non-primitive class or struct, not abstract, not object itself)
        if (!actual.IsPrimitive && actual != typeof(object) && !actual.IsAbstract &&
            (actual.IsClass || (actual.IsValueType && !actual.IsEnum)))
        {
            return GenerateForType(actual, depth + 1);
        }

        return Typed("string", isNullable);
    }

    private static Type? GetDictionaryValueType(Type type)
    {
        if (!type.IsGenericType)
            return null;

        var def = type.GetGenericTypeDefinition();
        if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) ||
            def == typeof(IReadOnlyDictionary<,>))
            return type.GetGenericArguments()[1];

        return null;
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (!type.IsGenericType)
            return null;

        var def = type.GetGenericTypeDefinition();
        if (def == typeof(List<>) || def == typeof(IList<>) ||
            def == typeof(IReadOnlyList<>) || def == typeof(IEnumerable<>) ||
            def == typeof(ICollection<>) || def == typeof(IReadOnlyCollection<>))
            return type.GetGenericArguments()[0];

        return null;
    }

    private static Dictionary<string, object> Typed(string jsonType, bool isNullable) =>
        new() { ["type"] = isNullable ? new[] { jsonType, "null" } : (object)jsonType };

    private static Dictionary<string, object> TypedWithFormat(string jsonType, string format, bool isNullable) =>
        new()
        {
            ["type"] = isNullable ? new[] { jsonType, "null" } : (object)jsonType,
            ["format"] = format
        };
}

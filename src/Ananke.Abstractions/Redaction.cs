using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Ananke.Abstractions;

/// <summary>
/// Masks the properties a type marks with <see cref="RedactAttribute"/>: in a value as it is
/// serialized (<see cref="Masking"/>), and in JSON text whose types are known
/// (<see cref="Mask(string, IReadOnlyDictionary{string, Type})"/>).
/// </summary>
/// <remarks>
/// <para>
/// A type's shape is read as the serializer writes it, with <see cref="AnankeJson.Display"/>: its
/// properties, their names, and whether it is an object, a list or a map. Names in JSON text are
/// matched ignoring case, as <see cref="AnankeJson"/> reads them.
/// </para>
/// <para>
/// <b>JSON text that cannot be read as its type is masked whole</b> — text that does not parse, or a
/// value where its type has an object or a list. Whatever it holds cannot be told apart from a marked
/// value, and a record that kept it would keep the value.
/// </para>
/// </remarks>
public static class Redaction
{
    /// <summary>What a marked property's value is replaced by.</summary>
    public const string Marker = "[redacted]";

    private static readonly ConcurrentDictionary<Type, bool> Marking = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, Member>> Members = new();
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> Maskings = new();

    /// <summary>
    /// <paramref name="options"/> as they are, except that every marked property is written as
    /// <see cref="Marker"/>. A null value is written as null: it holds nothing to mask.
    /// </summary>
    /// <remarks>For writing only: a masked value cannot be read back.</remarks>
    public static JsonSerializerOptions Masking(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Maskings.GetValue(options, static options =>
        {
            var masking = new JsonSerializerOptions(options)
            {
                TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
                    .WithAddedModifier(MaskMarked)
            };
            masking.MakeReadOnly();
            return masking;
        });
    }

    /// <summary>
    /// <paramref name="json"/>, an object whose members' types are given by name in
    /// <paramref name="members"/> — a tool call's arguments, say — with each marked property's value
    /// replaced by <see cref="Marker"/>. A member not named there is kept as written, and so is the
    /// whole text when no type marks anything or it is empty.
    /// </summary>
    public static string Mask(string json, IReadOnlyDictionary<string, Type> members)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(members);

        if (string.IsNullOrWhiteSpace(json) || !members.Values.Any(Marks))
            return json;

        var declared = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type) in members)
        {
            if (!declared.TryAdd(name, new Member(Marked: false, type)) && Marks(type))
                declared[name] = new Member(Marked: false, type);
        }

        try
        {
            var node = JsonNode.Parse(json);
            return node is JsonObject arguments ? MaskMembers(arguments, declared).ToJsonString() : Marker;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // Also a duplicate property name, which a model can write and JsonObject refuses.
            return Marker;
        }
    }

    /// <summary>Whether a property is marked. Every rule that asks this asks it here.</summary>
    internal static bool IsMarked(MemberInfo property) =>
        Attribute.IsDefined(property, typeof(RedactAttribute), inherit: true);

    private static void MaskMarked(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in info.Properties)
        {
            if (property.AttributeProvider is MemberInfo member && IsMarked(member))
                property.CustomConverter = (JsonConverter)Activator.CreateInstance(
                    typeof(MarkerConverter<>).MakeGenericType(property.PropertyType))!;
        }
    }

    /// <summary>Writes any value as <see cref="Marker"/>; a masked value cannot be read back.</summary>
    private sealed class MarkerConverter<T> : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("A value written masked cannot be read back.");

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Marker);
    }

    /// <summary>Whether <paramref name="type"/> marks a property, its own or one of a type it holds.</summary>
    private static bool Marks(Type type) => Marking.GetOrAdd(type, static type => Reaches(type, []));

    private static bool Reaches(Type type, HashSet<Type> visiting)
    {
        if (Written(type) is not { } info)
            return false;

        return info.Kind switch
        {
            JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary =>
                info.ElementType is { } element && Reaches(element, visiting),
            JsonTypeInfoKind.Object => visiting.Add(info.Type)
                && MembersOf(info).Values.Any(member => member.Marked || Reaches(member.Type, visiting)),
            _ => false
        };
    }

    /// <summary>
    /// <paramref name="node"/> masked in place, or the marker in its stead when it is not shaped like
    /// <paramref name="type"/>.
    /// </summary>
    private static JsonNode? Mask(JsonNode? node, Type type)
    {
        if (node is null || !Marks(type) || Written(type) is not { } info)
            return node;

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Dictionary when node is JsonObject map:
                foreach (var (name, value) in map.ToList())
                    Replace(map, name, value, Mask(value, info.ElementType!));
                return node;

            case JsonTypeInfoKind.Enumerable when node is JsonArray list:
                for (var i = 0; i < list.Count; i++)
                {
                    var masked = Mask(list[i], info.ElementType!);
                    if (!ReferenceEquals(masked, list[i]))
                        list[i] = masked;
                }
                return node;

            case JsonTypeInfoKind.Object when node is JsonObject held:
                return MaskMembers(held, MembersOf(info));

            default:
                return MarkerNode();
        }
    }

    private static JsonObject MaskMembers(JsonObject value, IReadOnlyDictionary<string, Member> members)
    {
        foreach (var (name, held) in value.ToList())
        {
            if (!members.TryGetValue(name, out var member))
                continue;

            // A marked value that is null holds nothing to mask.
            Replace(value, name, held, member.Marked ? held is null ? null : MarkerNode() : Mask(held, member.Type));
        }

        return value;
    }

    private static void Replace(JsonObject value, string name, JsonNode? held, JsonNode? masked)
    {
        if (!ReferenceEquals(masked, held))
            value[name] = masked;
    }

    private static JsonValue MarkerNode() => JsonValue.Create(Marker);

    /// <summary>How the serializer writes <paramref name="type"/>; <see langword="null"/> for a type it cannot.</summary>
    private static JsonTypeInfo? Written(Type type)
    {
        try
        {
            return AnankeJson.Display.GetTypeInfo(Nullable.GetUnderlyingType(type) ?? type);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The properties the serializer writes of an object type, by name.</summary>
    private static IReadOnlyDictionary<string, Member> MembersOf(JsonTypeInfo info) =>
        Members.GetOrAdd(info.Type, static (_, info) =>
        {
            var members = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in info.Properties)
            {
                var member = new Member(
                    property.AttributeProvider is MemberInfo declared && IsMarked(declared), property.PropertyType);

                if (!members.TryAdd(property.Name, member) && member.Marked)
                    members[property.Name] = member;
            }

            return members;
        }, info);

    private readonly record struct Member(bool Marked, Type Type);
}

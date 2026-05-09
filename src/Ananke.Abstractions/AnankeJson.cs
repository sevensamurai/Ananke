using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ananke.Abstractions;

/// <summary>
/// Named <see cref="JsonSerializerOptions"/> profiles used across Ananke packages.
/// </summary>
/// <remarks>
/// <para>
/// <list type="bullet">
///   <item>
///     <term><see cref="Wire"/></term>
///     <description>
///     Provider API payloads and inter-service communication. Uses
///     <see cref="JsonNamingPolicy.SnakeCaseLower"/> to match most REST APIs.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Storage"/></term>
///     <description>
///     Persistence (file registries, checkpoint stores, score stores).
///     Human-readable: indented, camelCase keys, enum strings.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Display"/></term>
///     <description>
///     Tool result payloads and agent-facing JSON. CamelCase, compact (not indented).
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// All profiles are read-only singletons. Do not mutate them; create a copy via
/// <c>new JsonSerializerOptions(AnankeJson.Wire)</c> if customisation is required.
/// </para>
/// </remarks>
public static class AnankeJson
{
    /// <summary>
    /// Wire format: snake_case keys, enum strings, no indentation.
    /// Suitable for provider REST API payloads and inter-service messaging.
    /// </summary>
    public static JsonSerializerOptions Wire { get; } = Build(
        namingPolicy: JsonNamingPolicy.SnakeCaseLower,
        writeIndented: false);

    /// <summary>
    /// Storage format: camelCase keys, enum strings, indented for human readability.
    /// Suitable for file-backed registries, checkpoint stores, and score stores.
    /// </summary>
    public static JsonSerializerOptions Storage { get; } = Build(
        namingPolicy: JsonNamingPolicy.CamelCase,
        writeIndented: true);

    /// <summary>
    /// Display format: camelCase keys, enum strings, compact (not indented).
    /// Suitable for tool result payloads and agent-facing structured output.
    /// </summary>
    public static JsonSerializerOptions Display { get; } = Build(
        namingPolicy: JsonNamingPolicy.CamelCase,
        writeIndented: false);

    private static JsonSerializerOptions Build(JsonNamingPolicy namingPolicy, bool writeIndented)
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = namingPolicy,
            WriteIndented = writeIndented,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        opts.MakeReadOnly();
        return opts;
    }
}

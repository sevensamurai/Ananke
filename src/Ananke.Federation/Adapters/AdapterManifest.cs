using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ananke.Federation.Adapters;

/// <summary>
/// JSON sidecar that every adapter installer writes alongside its DLLs.
/// Read by <c>PlatformHost</c> to validate compatibility before loading the assembly.
/// </summary>
/// <remarks>
/// File name convention: <c>&lt;id&gt;.adapter.json</c> (e.g. <c>azure-ai.adapter.json</c>).
/// </remarks>
public sealed record AdapterManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Short machine-readable identifier for the adapter (e.g. <c>"azure-ai"</c>).
    /// Must match the platform identifier used in <see cref="Ananke.Federation.Deployment.FederationDeployerRegistry"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name (e.g. <c>"Azure AI Agent Service"</c>).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Semver version of the installed adapter package (e.g. <c>"0.8.0"</c>).</summary>
    public required string Version { get; init; }

    /// <summary>
    /// Inclusive minimum <c>nnke-platform</c> version this adapter is compatible with,
    /// expressed as <c>"major.minor"</c> (e.g. <c>"0.8"</c>).
    /// A running CLI whose <c>major.minor</c> is lower than this value will skip the adapter.
    /// </summary>
    public required string MinCliVersion { get; init; }

    /// <summary>
    /// Exclusive upper bound for <c>nnke-platform</c> version compatibility,
    /// expressed as <c>"major.minor"</c> (e.g. <c>"1.0"</c>).
    /// A running CLI whose <c>major.minor</c> is equal to or higher than this value will skip the adapter.
    /// <see langword="null"/> means no upper bound.
    /// </summary>
    public string? MaxCliVersionExclusive { get; init; }

    /// <summary>
    /// File name (not path) of the DLL whose module initializer registers the factory
    /// into <see cref="Ananke.Federation.Deployment.FederationDeployerRegistry"/>
    /// (e.g. <c>"nnke-platform-azure.dll"</c>).
    /// </summary>
    public required string EntryAssembly { get; init; }

    // ── serialization helpers ─────────────────────────────────────────────────

    /// <summary>Deserializes an <see cref="AdapterManifest"/> from a JSON string.</summary>
    public static AdapterManifest FromJson(string json) =>
        JsonSerializer.Deserialize<AdapterManifest>(json, JsonOptions)
            ?? throw new JsonException("Adapter manifest deserialized to null.");

    /// <summary>Serializes this manifest to an indented JSON string.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    // ── compatibility check ───────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="cliVersion"/> falls within
    /// the [<see cref="MinCliVersion"/>, <see cref="MaxCliVersionExclusive"/>) range.
    /// Only <c>major.minor</c> components are compared.
    /// </summary>
    public bool IsCompatibleWith(System.Version cliVersion)
    {
        ArgumentNullException.ThrowIfNull(cliVersion);

        if (!TryParseMinor(MinCliVersion, out var min))
            return false;

        if (cliVersion < min)
            return false;

        if (MaxCliVersionExclusive is not null)
        {
            if (!TryParseMinor(MaxCliVersionExclusive, out var max))
                return false;

            if (cliVersion >= max)
                return false;
        }

        return true;
    }

    private static bool TryParseMinor(string value, out System.Version parsedVersion)
    {
        // Ensure at least "major.minor" for Version.Parse
        var normalized = value.Contains('.') ? value : value + ".0";
        return System.Version.TryParse(normalized, out parsedVersion!);
    }
}

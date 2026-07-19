using System.Text.Json;

namespace Ananke.Abstractions.Agents;

/// <summary>
/// Lifecycle metadata for one non-<see cref="ModelStatus.Current"/> model, as recorded in
/// <c>model-lifecycle.json</c>.
/// </summary>
/// <param name="Id">The model identifier (matches a <see cref="Models"/> constant value).</param>
/// <param name="Status">The model's lifecycle stage.</param>
/// <param name="ReplacedBy">
/// The recommended replacement model identifier, or <see langword="null"/> if none is recorded.
/// Always points at a <see cref="ModelStatus.Current"/> model, never at an intermediate
/// <see cref="ModelStatus.Legacy"/> one.
/// </param>
public sealed record ModelLifecycleEntry(string Id, ModelStatus Status, string? ReplacedBy);

/// <summary>
/// Reads <c>model-lifecycle.json</c> — the single source of truth for which known models are
/// <see cref="ModelStatus.Legacy"/>, <see cref="ModelStatus.Deprecated"/>, or
/// <see cref="ModelStatus.Retired"/>, and what replaces each. A model absent from this data is
/// <see cref="ModelStatus.Current"/>.
/// </summary>
/// <remarks>
/// The same physical file is consumed two ways so the three lifecycle-aware pieces of the
/// framework (<c>Ananke.Design.ModelCatalog</c>'s manifest validation, <c>Ananke.Orchestration
/// .Agents.Routing.ModelCatalog</c>'s capability-routing templates, and the <c>ANNKE002</c>
/// Roslyn analyzer's literal-string check) cannot drift against each other:
/// <list type="bullet">
///   <item>At runtime, this class reads it as an embedded resource (see the
///   <c>EmbeddedResource</c> item in <c>Ananke.Abstractions.csproj</c>).</item>
///   <item>At analyzer compile time, <c>Ananke.Analyzers</c> reads the same file via an
///   <c>AdditionalFiles</c> item pointing at this exact path.</item>
/// </list>
/// </remarks>
public static class ModelLifecycleData
{
    private const string ResourceName = "Ananke.Abstractions.Agents.model-lifecycle.json";

    /// <summary>
    /// All non-Current entries, keyed by model id (case-insensitive). A model not present here
    /// is <see cref="ModelStatus.Current"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, ModelLifecycleEntry> Entries { get; } = Load();

    private static IReadOnlyDictionary<string, ModelLifecycleEntry> Load()
    {
        using var stream = typeof(ModelLifecycleData).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found in {typeof(ModelLifecycleData).Assembly.FullName}.");

        using var document = JsonDocument.Parse(stream);
        var entries = new Dictionary<string, ModelLifecycleEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = element.GetProperty("id").GetString()!;
            var status = Enum.Parse<ModelStatus>(element.GetProperty("status").GetString()!);
            var replacedBy = element.TryGetProperty("replacedBy", out var replacedByValue)
                ? replacedByValue.GetString()
                : null;

            entries[id] = new ModelLifecycleEntry(id, status, replacedBy);
        }

        return entries;
    }
}

using System.Text.Json;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// Persists plan trees as JSON files, one per plan, so a tree outlives the process that built it.
/// </summary>
/// <remarks>
/// <para>
/// Durability is not decoration here: the argument for reading a tree instead of passing records is
/// that <em>a loss becomes recoverable</em>, and that only holds while the tree still exists. A run
/// that dies mid-plan leaves every verdict it recorded readable by the run that resumes it.
/// </para>
/// <para>
/// Writes go to a temporary file and are then moved into place, so a process killed mid-write leaves
/// the previous version intact rather than a truncated one. The plan id is reduced to a safe file
/// name — it is an identifier, never a path — and fingerprinted, so two ids that sanitise alike do
/// not quietly share a file.
/// </para>
/// </remarks>
public sealed class FilePlanTreeStore : IPlanTreeStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _root;

    /// <summary>Creates a store rooted at <paramref name="root"/>, creating the directory if needed.</summary>
    public FilePlanTreeStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public async Task SaveAsync(PlanTree tree, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var path = PathFor(tree.PlanId);
        var temp = path + ".tmp";

        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(tree, Json), ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    /// <inheritdoc />
    public async Task<PlanTree?> LoadAsync(string planId, CancellationToken ct = default)
    {
        var path = PathFor(planId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<PlanTree>(json, Json);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string planId, CancellationToken ct = default)
    {
        var path = PathFor(planId);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string PathFor(string planId) =>
        Path.Combine(_root, FileStoreNaming.For(planId, ".plan.json"));
}

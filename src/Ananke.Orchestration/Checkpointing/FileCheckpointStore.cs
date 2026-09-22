using System.Text.Json;

namespace Ananke.Orchestration.Checkpointing;

/// <summary>
/// Persists checkpoints as JSON files, one per execution, so a paused run outlives its process.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tier's thesis needs this to be true.</b> A workflow that runs for a long time and asks a
/// person only when it must is a workflow that spends most of its life waiting — and what a person is
/// being asked does not live in the plan tree. The question, the options it offers, the change count
/// and the halt are all <c>PlanCoordination</c>, which is workflow state; with only an in-memory
/// store behind it, the demonstrated pause required the process to stay alive while somebody thought.
/// </para>
/// <para>
/// Writes go to a temporary file and are then moved into place, so a process killed mid-write leaves
/// the previous checkpoint intact rather than a truncated one — the same care
/// <see cref="Planning.FilePlanTreeStore"/> takes, for the same reason, and the two are usually
/// wired together.
/// </para>
/// <para>
/// <b>Expiry is read from the file rather than tracked beside it.</b> A store whose index of what has
/// expired lives in memory would answer differently after a restart, which is the one thing a durable
/// store may not do.
/// </para>
/// </remarks>
public sealed class FileCheckpointStore : ICheckpointStore
{
    private const string Suffix = ".checkpoint.json";

    private readonly string _root;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a store rooted at <paramref name="root"/>, creating the directory if needed.</summary>
    /// <param name="root">The folder checkpoints are written to.</param>
    /// <param name="timeProvider">
    /// Clock used to evaluate checkpoint expiry. Defaults to <see cref="TimeProvider.System"/>;
    /// inject a fake in tests to assert TTL behaviour without sleeping.
    /// </param>
    public FileCheckpointStore(string root, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = root;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public async Task SaveAsync<TState>(Checkpoint<TState> checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var path = PathFor(checkpoint.ExecutionId);
        var temp = path + ".tmp";

        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(checkpoint), ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    /// <inheritdoc />
    public async Task<Checkpoint<TState>?> LoadAsync<TState>(
        string executionId, CancellationToken ct = default)
    {
        var path = PathFor(executionId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var checkpoint = JsonSerializer.Deserialize<Checkpoint<TState>>(json);

        // Expired is indistinguishable from absent, exactly as the in-memory store has it, and the
        // file goes with the answer so the folder does not accumulate what nothing will return.
        if (checkpoint is null || checkpoint.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            File.Delete(path);
            return null;
        }

        return checkpoint;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string executionId, CancellationToken ct = default)
    {
        var path = PathFor(executionId);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string executionId, CancellationToken ct = default)
    {
        var path = PathFor(executionId);
        if (!File.Exists(path))
            return false;

        if (await ExpiryOf(path, ct).ConfigureAwait(false) > _timeProvider.GetUtcNow())
            return true;

        File.Delete(path);
        return false;
    }

    /// <inheritdoc />
    public async Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var path in Directory.EnumerateFiles(_root, $"*{Suffix}"))
        {
            if (await ExpiryOf(path, ct).ConfigureAwait(false) <= now)
                File.Delete(path);
        }
    }

    /// <summary>
    /// When the checkpoint at <paramref name="path"/> expires, without knowing what state it holds.
    /// </summary>
    /// <remarks>
    /// <see cref="ExistsAsync"/> and <see cref="CleanupExpiredAsync"/> are not generic — they answer
    /// about a checkpoint whose <c>TState</c> nobody has named — so they read the one field they need
    /// and leave the rest of the document alone.
    /// </remarks>
    private static async Task<DateTimeOffset> ExpiryOf(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Expiry>(json)?.ExpiresAt ?? DateTimeOffset.MinValue;
    }

    private string PathFor(string executionId) =>
        Path.Combine(_root, FileStoreNaming.For(executionId, Suffix));

    private sealed record Expiry
    {
        public DateTimeOffset ExpiresAt { get; init; }
    }
}

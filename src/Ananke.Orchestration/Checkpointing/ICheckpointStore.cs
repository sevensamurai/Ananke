namespace Ananke.Orchestration.Checkpointing;

/// <summary>
/// Persistent storage for workflow checkpoints, enabling pause-and-resume semantics.
/// </summary>
/// <remarks>
/// <para>
/// Built-in implementations:
/// <list type="bullet">
///   <item><see cref="InMemoryCheckpointStore"/> — tests and single-process scenarios (state lost on restart)</item>
///   <item><see cref="FileCheckpointStore"/> — one JSON file per execution, so a paused run outlives
///   its process. What a workflow is waiting to be told lives in workflow state, so a run that pauses
///   for a person needs this or something like it to survive the wait</item>
/// </list>
/// </para>
/// <para>
/// <b>Distributed deployments:</b> implement this interface backed by Redis, SQL, or any store that
/// several processes can reach. A Redis implementation can delegate to <c>RedisDataAdapter</c> from
/// <c>Ananke.Redis</c> for get/set/delete and use <c>EXPIREAT</c> for TTL-based expiry
/// (maps to <see cref="Checkpoint{TState}.ExpiresAt"/>). Key format suggestion:
/// <c>checkpoint:{executionId}</c>. Serialization: <see cref="System.Text.Json.JsonSerializer"/>.
/// </para>
/// </remarks>
public interface ICheckpointStore
{
    /// <summary>Persists or overwrites the checkpoint for the given execution.</summary>
    Task SaveAsync<TState>(Checkpoint<TState> checkpoint, CancellationToken ct = default);

    /// <summary>
    /// Loads the checkpoint for <paramref name="executionId"/>, or <see langword="null"/> if it does
    /// not exist or has expired.
    /// </summary>
    Task<Checkpoint<TState>?> LoadAsync<TState>(string executionId, CancellationToken ct = default);

    /// <summary>Deletes the checkpoint for <paramref name="executionId"/> if it exists.</summary>
    Task DeleteAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// Returns <see langword="true"/> if a non-expired checkpoint exists for
    /// <paramref name="executionId"/>.
    /// </summary>
    Task<bool> ExistsAsync(string executionId, CancellationToken ct = default);

    /// <summary>Removes all checkpoints whose <see cref="Checkpoint{TState}.ExpiresAt"/> is in the past.</summary>
    Task CleanupExpiredAsync(CancellationToken ct = default);
}

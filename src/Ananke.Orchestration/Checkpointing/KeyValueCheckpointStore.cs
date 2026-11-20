using System.Text.Json;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Checkpointing;

/// <summary>
/// <see cref="ICheckpointStore"/> over an <see cref="IKeyValueDataAdapter"/>: each checkpoint is one JSON string
/// keyed by <c>{prefix}:{executionId}</c>, written with an expiry taken from
/// <see cref="Checkpoint{TState}.ExpiresAt"/>. Over Redis (the adapter <c>AddRedis</c> registers) several processes
/// share the checkpoints; over <see cref="InMemoryDistributedLock"/> they live in this process only.
/// </summary>
/// <remarks>
/// The adapter removes a checkpoint once its expiry passes, so <see cref="CleanupExpiredAsync"/> has nothing to do.
/// <see cref="LoadAsync{TState}"/> and <see cref="ExistsAsync"/> also check <see cref="Checkpoint{TState}.ExpiresAt"/>
/// against this store's clock, for a checkpoint the adapter has not removed yet.
/// </remarks>
/// <param name="store">The key-value store the checkpoints are written to, which the caller keeps owning.</param>
/// <param name="prefix">Key prefix for checkpoint keys. Defaults to <c>"ananke:checkpoint"</c>.</param>
/// <param name="logger">Optional logger.</param>
/// <param name="clock">Clock for the expiry of a checkpoint. Defaults to <see cref="TimeProvider.System"/>.</param>
public sealed class KeyValueCheckpointStore(
    IKeyValueDataAdapter store,
    string prefix = "ananke:checkpoint",
    ILogger<KeyValueCheckpointStore>? logger = null,
    TimeProvider? clock = null) : ICheckpointStore
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IKeyValueDataAdapter _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly string _prefix = string.IsNullOrWhiteSpace(prefix)
        ? throw new ArgumentException("The key prefix cannot be empty.", nameof(prefix))
        : prefix;
    private readonly ILogger<KeyValueCheckpointStore> _logger = logger ?? NullLogger<KeyValueCheckpointStore>.Instance;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private string Key(string executionId) => $"{_prefix}:{executionId}";

    /// <inheritdoc />
    /// <remarks>
    /// A checkpoint whose <see cref="Checkpoint{TState}.ExpiresAt"/> has already passed is removed rather than
    /// written; one that expires at <see cref="DateTimeOffset.MaxValue"/> is written with no expiry.
    /// </remarks>
    public async Task SaveAsync<TState>(Checkpoint<TState> checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ct.ThrowIfCancellationRequested();

        var key = Key(checkpoint.ExecutionId);
        TimeSpan? expiry = null;
        if (checkpoint.ExpiresAt != DateTimeOffset.MaxValue)
        {
            expiry = checkpoint.ExpiresAt - _clock.GetUtcNow();
            // The adapter refuses an expiry of zero or less, and an old value left in place would load again.
            if (expiry <= TimeSpan.Zero)
            {
                await _store.RemoveAsync(key, ct).ConfigureAwait(false);
                return;
            }
        }

        await _store.SetValueAsync(key, JsonSerializer.Serialize(checkpoint), expiry, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>A value that is not a checkpoint is logged as a warning and loads as <see langword="null"/>.</remarks>
    public async Task<Checkpoint<TState>?> LoadAsync<TState>(string executionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var checkpoint = await ReadAsync<Checkpoint<TState>>(executionId, ct).ConfigureAwait(false);
        return checkpoint is not null && checkpoint.ExpiresAt > _clock.GetUtcNow() ? checkpoint : null;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string executionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _store.RemoveAsync(Key(executionId), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>A value that is not a checkpoint is logged as a warning and answers <see langword="false"/>.</remarks>
    public async Task<bool> ExistsAsync(string executionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var expiry = await ReadAsync<Expiry>(executionId, ct).ConfigureAwait(false);
        return expiry is not null && expiry.ExpiresAt > _clock.GetUtcNow();
    }

    /// <inheritdoc />
    /// <remarks>Nothing to do: the adapter removes each checkpoint once its expiry passes.</remarks>
    public Task CleanupExpiredAsync(CancellationToken ct = default) =>
        ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;

    /// <summary>
    /// The value under <paramref name="executionId"/>'s key as <typeparamref name="T"/>; <see langword="null"/> when
    /// the key is missing or its value is not valid JSON, which is logged as a warning.
    /// </summary>
    private async Task<T?> ReadAsync<T>(string executionId, CancellationToken ct) where T : class
    {
        var json = await _store.GetValueAsync(Key(executionId), ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Corrupt checkpoint for execution {ExecutionId}", executionId);
            return null;
        }
    }

    /// <summary>
    /// The one field of a checkpoint <see cref="ExistsAsync"/> needs, read without knowing its state's type.
    /// </summary>
    private sealed record Expiry
    {
        public DateTimeOffset ExpiresAt { get; init; }
    }
}

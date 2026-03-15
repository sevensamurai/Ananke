using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Checkpointing;

/// <summary>
/// File-based checkpoint store for single-instance deployments.
/// Each checkpoint is serialized to a JSON file in a configurable directory.
/// Suitable for local development and single-process scenarios.
/// For distributed deployments, implement <see cref="ICheckpointStore"/> backed by Redis or a database.
/// </summary>
public sealed class FileCheckpointStore : ICheckpointStore
{
    private readonly string _directory;
    private readonly ILogger<FileCheckpointStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public FileCheckpointStore(string directory, ILogger<FileCheckpointStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _logger = logger ?? NullLogger<FileCheckpointStore>.Instance;
        Directory.CreateDirectory(directory);
    }

    private string GetPath(string executionId) =>
        Path.Combine(_directory, $"{executionId}.json");

    public async Task SaveAsync<TState>(Checkpoint<TState> checkpoint, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        await File.WriteAllTextAsync(GetPath(checkpoint.ExecutionId), json, ct);
    }

    public async Task<Checkpoint<TState>?> LoadAsync<TState>(string executionId, CancellationToken ct = default)
    {
        var path = GetPath(executionId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        var checkpoint = JsonSerializer.Deserialize<Checkpoint<TState>>(json);

        if (checkpoint is null)
            return null;

        if (checkpoint.ExpiresAt != DateTimeOffset.MaxValue && checkpoint.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            File.Delete(path);
            return null;
        }

        return checkpoint;
    }

    public Task DeleteAsync(string executionId, CancellationToken ct = default)
    {
        var path = GetPath(executionId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(string executionId, CancellationToken ct = default)
    {
        var path = GetPath(executionId);
        if (!File.Exists(path))
            return false;

        // Lazy expiry check on existence queries
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ExpiresAt", out var expiresEl) &&
                expiresEl.TryGetDateTimeOffset(out var expiresAt) &&
                expiresAt <= DateTimeOffset.UtcNow)
            {
                File.Delete(path);
                return false;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Corrupt checkpoint file for execution {ExecutionId}, treating as exists", executionId);
        }

        return true;
    }

    public async Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var file in Directory.GetFiles(_directory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ExpiresAt", out var expiresEl) &&
                    expiresEl.TryGetDateTimeOffset(out var expiresAt) &&
                    expiresAt <= now)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to process checkpoint file '{File}' during cleanup", file);
            }
        }
    }
}

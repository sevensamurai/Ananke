using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ananke.Abstractions;
using Ananke.Federation.Paths;

namespace Ananke.Federation.Deployment;

/// <summary>
/// File-backed implementation of <see cref="IDeploymentRegistry"/> that persists deployment
/// records as JSON. Suitable for CLI tools where cross-invocation state is required.
/// </summary>
/// <remarks>
/// <para>
/// Writes are atomic via a temp-file + rename pattern. Concurrent CLI invocations are
/// coordinated through a named <see cref="Mutex"/> keyed on the canonical file path.
/// </para>
/// <para>
/// The JSON file includes a <c>schemaVersion</c> header to support future migrations.
/// The default storage location is <c>%USERPROFILE%\.nnke-platform\deployments.json</c>
/// on Windows, or <c>$XDG_STATE_HOME/.nnke-platform/deployments.json</c> (falling back to
/// <c>~/.local/state/.nnke-platform/deployments.json</c>) on Linux/macOS.
/// </para>
/// </remarks>
public sealed class JsonFileDeploymentRegistry : IDeploymentRegistry, IDisposable
{
    internal const int CurrentSchemaVersion = 1;

    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = AnankeJson.Storage;

    private readonly string _filePath;
    private readonly Mutex _mutex;

    /// <summary>
    /// The default path used when no explicit path is provided.
    /// </summary>
    public static string DefaultPath => AnankePaths.DeploymentsFile;

    /// <summary>
    /// Initialises a new instance using <see cref="DefaultPath"/>.
    /// </summary>
    public JsonFileDeploymentRegistry() : this(DefaultPath) { }

    /// <summary>
    /// Initialises a new instance writing to <paramref name="filePath"/>.
    /// </summary>
    /// <param name="filePath">Absolute path to the JSON registry file.</param>
    public JsonFileDeploymentRegistry(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _mutex = new Mutex(initiallyOwned: false, BuildMutexName(filePath));
    }

    /// <inheritdoc />
    public Task RegisterAsync(DeploymentRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Task.Run(() => WithLock(() =>
        {
            var schema = ReadFile();
            if (schema.Deployments.Exists(d => d.DeploymentId == record.DeploymentId))
                throw new InvalidOperationException($"Deployment '{record.DeploymentId}' already exists.");
            schema.Deployments.Add(record);
            WriteFile(schema);
        }), ct);
    }

    /// <inheritdoc />
    public Task<DeploymentRecord?> GetAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        return Task.Run(() => WithLock(() =>
            ReadFile().Deployments.Find(d => d.DeploymentId == deploymentId)), ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeploymentRecord>> ListAsync(string? workflowName = null, CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<DeploymentRecord>>(() => WithLock(() =>
        {
            var deployments = ReadFile().Deployments;
            return workflowName is null
                ? deployments.ToList()
                : deployments.Where(d => d.WorkflowName == workflowName).ToList();
        }), ct);
    }

    /// <inheritdoc />
    public Task UpdateStatusAsync(string deploymentId, DeploymentStatus status, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);
        return Task.Run(() => WithLock(() =>
        {
            var schema = ReadFile();
            var idx = schema.Deployments.FindIndex(d => d.DeploymentId == deploymentId);
            if (idx < 0)
                throw new KeyNotFoundException($"Deployment '{deploymentId}' not found.");

            schema.Deployments[idx] = schema.Deployments[idx] with
            {
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            WriteFile(schema);
        }), ct);
    }

    /// <inheritdoc />
    public Task UpdateAsync(DeploymentRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Task.Run(() => WithLock(() =>
        {
            var schema = ReadFile();
            var idx = schema.Deployments.FindIndex(d => d.DeploymentId == record.DeploymentId);
            if (idx < 0)
                throw new KeyNotFoundException($"Deployment '{record.DeploymentId}' not found.");

            schema.Deployments[idx] = record;
            WriteFile(schema);
        }), ct);
    }

    /// <summary>Releases the named mutex held by this instance.</summary>
    public void Dispose() => _mutex.Dispose();

    // ── private helpers ──────────────────────────────────────────────────────

    private void WithLock(Action action)
    {
        if (!_mutex.WaitOne(MutexTimeout))
            throw new TimeoutException(
                $"Could not acquire registry lock within {MutexTimeout.TotalSeconds}s. " +
                $"Another process may be holding the lock on '{_filePath}'.");
        try { action(); }
        finally { _mutex.ReleaseMutex(); }
    }

    private T WithLock<T>(Func<T> func)
    {
        if (!_mutex.WaitOne(MutexTimeout))
            throw new TimeoutException(
                $"Could not acquire registry lock within {MutexTimeout.TotalSeconds}s. " +
                $"Another process may be holding the lock on '{_filePath}'.");
        try { return func(); }
        finally { _mutex.ReleaseMutex(); }
    }

    private FileSchema ReadFile()
    {
        if (!File.Exists(_filePath))
            return new FileSchema();

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<FileSchema>(json, JsonOptions) ?? new FileSchema();
    }

    private void WriteFile(FileSchema schema)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(schema, JsonOptions));
        File.Move(tmp, _filePath, overwrite: true);
    }

    private static string BuildMutexName(string filePath)
    {
        var canonical = Path.GetFullPath(filePath).ToUpperInvariant();
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(canonical)));
        return $"nnke-platform-registry-{hash}";
    }

    // ── JSON schema ──────────────────────────────────────────────────────────

    internal sealed class FileSchema
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonPropertyName("deployments")]
        public List<DeploymentRecord> Deployments { get; set; } = [];
    }
}

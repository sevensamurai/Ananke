using System.Text.Json;

namespace Ananke.Skills;

/// <summary>
/// Simple file-based <see cref="ISkillScoreStore"/> that persists scores to a JSON file.
/// Suitable for single-process deployments and demos.
/// </summary>
public sealed class JsonFileScoreStore : ISkillScoreStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonFileScoreStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task RecordVoteAsync(string skillId, VoteDirection direction, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var scores = await LoadAsync(ct).ConfigureAwait(false);

            var current = scores.GetValueOrDefault(skillId) ?? new ScoreEntry();
            scores[skillId] = direction switch
            {
                VoteDirection.Up => current with { UpVotes = current.UpVotes + 1 },
                VoteDirection.Down => current with { DownVotes = current.DownVotes + 1 },
                _ => current
            };

            await SaveAsync(scores, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SkillScore> GetScoreAsync(string skillId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var scores = await LoadAsync(ct).ConfigureAwait(false);
            return scores.TryGetValue(skillId, out var entry)
                ? new SkillScore(entry.UpVotes, entry.DownVotes)
                : new SkillScore();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, SkillScore>> GetAllScoresAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var scores = await LoadAsync(ct).ConfigureAwait(false);
            return scores.ToDictionary(
                kv => kv.Key,
                kv => new SkillScore(kv.Value.UpVotes, kv.Value.DownVotes));
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, ScoreEntry>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return [];

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, ScoreEntry>>(stream, JsonOptions, ct)
            .ConfigureAwait(false) ?? [];
    }

    private async Task SaveAsync(Dictionary<string, ScoreEntry> scores, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, scores, JsonOptions, ct).ConfigureAwait(false);
    }

    private sealed record ScoreEntry(int UpVotes = 0, int DownVotes = 0);
}

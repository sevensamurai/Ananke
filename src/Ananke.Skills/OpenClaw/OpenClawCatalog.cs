using System.Diagnostics;
using System.Text.Json;
using Ananke.Orchestration.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Skills.OpenClaw;

/// <summary>
/// <see cref="ISkillCatalog"/> implementation that discovers skills from OpenClaw/ClawHub.
/// Skills are cached locally as a JSON catalog file. Resolution bridges CLI tools
/// via <see cref="CliProcessRunner"/> — no MCP or external protocol required.
/// </summary>
/// <remarks>
/// On first use, call <see cref="SyncAsync"/> to populate the local cache.
/// Between syncs, <see cref="SearchAsync"/> operates entirely offline.
/// </remarks>
public sealed class OpenClawCatalog : ISkillCatalog
{
    private readonly string _cacheDir;
    private readonly ISkillScoreStore? _scoreStore;
    private readonly TimeSpan _processTimeout;
    private readonly bool _enableVoting;
    private readonly ILogger _logger;

    private List<SkillDescriptor>? _cache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates an OpenClaw catalog with a local cache directory.
    /// </summary>
    /// <param name="cacheDir">Directory for cached catalog and scores. Created if missing.</param>
    /// <param name="scoreStore">Optional score store for skill voting. If null, scores are not persisted.</param>
    /// <param name="processTimeout">Timeout for CLI tool execution. Defaults to 30s.</param>
    /// <param name="enableVoting">When <c>true</c>, successful and failed tool executions automatically record up/down votes. Defaults to <c>false</c>.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public OpenClawCatalog(
        string cacheDir,
        ISkillScoreStore? scoreStore = null,
        TimeSpan? processTimeout = null,
        bool enableVoting = false,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);
        _cacheDir = cacheDir;
        _scoreStore = scoreStore;
        _processTimeout = processTimeout ?? CliProcessRunner.DefaultTimeout;
        _enableVoting = enableVoting;
        _logger = logger ?? NullLogger.Instance;
    }

    private string CatalogFilePath => Path.Combine(_cacheDir, "catalog.json");

    public async Task<IReadOnlyList<SkillDescriptor>> SearchAsync(
        string query,
        IReadOnlyList<string>? tags = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        var catalog = await EnsureCacheAsync(ct).ConfigureAwait(false);

        // Enrich with live scores if a store is available
        IReadOnlyDictionary<string, SkillScore>? scores = null;
        if (_scoreStore is not null)
            scores = await _scoreStore.GetAllScoresAsync(ct).ConfigureAwait(false);

        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var results = catalog
            .Select(skill =>
            {
                var liveScore = scores is not null && scores.TryGetValue(skill.Id, out var s)
                    ? s : skill.Score;
                var enriched = skill with { Score = liveScore };
                var relevance = ComputeRelevance(enriched, queryTerms, tags);
                return (skill: enriched, relevance);
            })
            .Where(x => x.relevance > 0)
            .OrderByDescending(x => x.skill.Score?.Net ?? 0)
            .ThenByDescending(x => x.relevance)
            .Take(limit)
            .Select(x => x.skill)
            .ToList();

        return results;
    }

    public Task<ToolDefinition> ResolveAsync(
        SkillDescriptor skill,
        CancellationToken ct = default)
    {
        var runner = skill.Install switch
        {
            SkillInstallMethod.Uvx => "uvx",
            SkillInstallMethod.Npx => "npx",
            _ => throw new NotSupportedException(
                $"Install method '{skill.Install}' is not yet supported. " +
                $"Currently supported: Uvx, Npx.")
        };

        var prerequisite = ToolPrerequisite.Binary(runner,
            runner == "uvx"
                ? "Install uv: winget install astral-sh.uv — see docs/guides/uv-setup-for-dotnet-developers.md"
                : $"Install {runner} to run this skill.");

        var parameters = skill.Parameters.Count > 0
            ? skill.Parameters
                .Where(p => !p.IsFlag)
                .Select(p => new ToolParameter(p.Name, p.Description, IsRequired: p.IsRequired))
                .ToList()
            : new List<ToolParameter> { new("query", "The search query or input for the tool", IsRequired: true) };

        var requiredParams = skill.Parameters.Count > 0
            ? skill.Parameters.Where(p => p.IsRequired && !p.IsFlag).Select(p => p.Name).ToHashSet()
            : new HashSet<string> { "query" };

        var tool = new ToolDefinition
        {
            Name = NormalizeToolName(skill.Name),
            Description = skill.Description,
            Parameters = parameters,
            Tags = skill.Tags.ToList(),
            Requires = [prerequisite],
            Execute = async (args, toolCt) =>
            {
                var cliArgs = BuildCliArguments(skill, args, requiredParams);
                var fullCommand = $"{skill.EffectivePackage} {cliArgs}";

                _logger.LogInformation(
                    "[Skill] Executing '{SkillName}': {Runner} {Command} (timeout: {Timeout}s)",
                    skill.Name, runner, fullCommand, _processTimeout.TotalSeconds);

                var sw = Stopwatch.StartNew();
                var result = await CliProcessRunner.RunAsync(
                    runner,
                    fullCommand,
                    workingDirectory: null,
                    timeout: _processTimeout,
                    ct: toolCt).ConfigureAwait(false);
                sw.Stop();

                if (result.Success)
                {
                    _logger.LogInformation(
                        "[Skill] '{SkillName}' succeeded in {ElapsedMs}ms (stdout: {Length} chars)",
                        skill.Name, sw.ElapsedMilliseconds, result.Stdout.Length);

                    if (_enableVoting && _scoreStore is not null)
                    {
                        await _scoreStore.RecordVoteAsync(skill.Id, VoteDirection.Up, toolCt).ConfigureAwait(false);
                        _logger.LogDebug("[Skill] Recorded Up vote for '{SkillId}'", skill.Id);
                    }

                    return ToolResult.Ok(result.Stdout.Trim());
                }

                var stderr = result.Stderr.Trim();

                _logger.LogWarning(
                    "[Skill] '{SkillName}' failed in {ElapsedMs}ms (exit {ExitCode}): {Stderr}",
                    skill.Name, sw.ElapsedMilliseconds, result.ExitCode,
                    stderr.Length > 200 ? stderr[..200] + "..." : stderr);

                if (_enableVoting && _scoreStore is not null && result.ExitCode != -1)
                {
                    await _scoreStore.RecordVoteAsync(skill.Id, VoteDirection.Down, toolCt).ConfigureAwait(false);
                    _logger.LogDebug("[Skill] Recorded Down vote for '{SkillId}'", skill.Id);
                }

                // Timeouts (exit -1 from CliProcessRunner) are the only truly transient
                // failures — the tool might succeed if the network is faster next time.
                // All other exit codes are deterministic: same args → same crash.
                return result.ExitCode == -1
                    ? ToolResult.Error($"{skill.Name} timed out after {_processTimeout.TotalSeconds:F0}s")
                    : ToolResult.Fatal($"{skill.Name} failed (exit {result.ExitCode}): {stderr}");
            }
        };

        return Task.FromResult(tool);
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        // Phase 1: load from local seed file. In a future phase this will fetch
        // from the ClawHub API or parse SKILL.md files from the GitHub repo.
        Directory.CreateDirectory(_cacheDir);

        if (File.Exists(CatalogFilePath))
        {
            await using var stream = File.OpenRead(CatalogFilePath);
            _cache = await JsonSerializer.DeserializeAsync<List<SkillDescriptor>>(stream, JsonOptions, ct)
                .ConfigureAwait(false) ?? [];
        }
        else
        {
            _cache = [];
            await SaveCatalogAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Adds skill descriptors to the local catalog cache and persists them.
    /// Use this to seed the catalog with known skills before remote sync is available.
    /// Existing skills with the same ID are updated (upserted).
    /// </summary>
    public async Task AddSkillsAsync(IEnumerable<SkillDescriptor> skills, CancellationToken ct = default)
    {
        var catalog = await EnsureCacheAsync(ct).ConfigureAwait(false);
        var indexById = new Dictionary<string, int>(catalog.Count);
        for (var i = 0; i < catalog.Count; i++)
            indexById[catalog[i].Id] = i;

        foreach (var skill in skills)
        {
            if (indexById.TryGetValue(skill.Id, out var idx))
                catalog[idx] = skill;
            else
                catalog.Add(skill);
        }

        await SaveCatalogAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<SkillDescriptor>> EnsureCacheAsync(CancellationToken ct)
    {
        if (_cache is not null)
            return _cache;

        await SyncAsync(ct).ConfigureAwait(false);
        return _cache!;
    }

    private async Task SaveCatalogAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheDir);
        await using var stream = File.Create(CatalogFilePath);
        await JsonSerializer.SerializeAsync(stream, _cache ?? [], JsonOptions, ct).ConfigureAwait(false);
    }

    private static int ComputeRelevance(
        SkillDescriptor skill,
        string[] queryTerms,
        IReadOnlyList<string>? tags)
    {
        var score = 0;
        var nameAndDesc = $"{skill.Name} {skill.Description}".ToLowerInvariant();
        var skillTags = skill.Tags.Select(t => t.ToLowerInvariant()).ToHashSet();

        foreach (var term in queryTerms)
        {
            var lower = term.ToLowerInvariant();
            if (nameAndDesc.Contains(lower, StringComparison.Ordinal))
                score += 2;
            if (skillTags.Contains(lower))
                score += 3;
        }

        if (tags is { Count: > 0 })
        {
            foreach (var tag in tags)
            {
                if (skillTags.Contains(tag.ToLowerInvariant()))
                    score += 5;
            }
        }

        return score;
    }

    private static string BuildCliArguments(
        SkillDescriptor skill,
        IReadOnlyDictionary<string, object?> args,
        HashSet<string> requiredParams)
    {
        var parts = new List<string>();

        // If skill has no declared params, pass the 'query' as a positional argument
        if (skill.Parameters.Count == 0)
        {
            if (args.TryGetValue("query", out var queryVal) && queryVal is not null)
                parts.Add($"\"{EscapeArg(queryVal.ToString()!)}\"");

            if (!string.IsNullOrWhiteSpace(skill.ExtraCliArgs))
                parts.Add(skill.ExtraCliArgs);

            return string.Join(' ', parts);
        }

        // Positional arguments first, then named flags
        foreach (var param in skill.Parameters.Where(p => p.IsPositional))
        {
            if (args.TryGetValue(param.Name, out var value) && value is not null)
                parts.Add($"\"{EscapeArg(value.ToString()!)}\"");
            else if (requiredParams.Contains(param.Name))
                parts.Add("\"\"");
        }

        foreach (var param in skill.Parameters.Where(p => !p.IsPositional && !p.IsFlag))
        {
            if (!args.TryGetValue(param.Name, out var value) || value is null)
            {
                if (requiredParams.Contains(param.Name))
                    parts.Add($"--{param.Name} \"\"");
                continue;
            }

            var strValue = value.ToString()!;
            parts.Add($"--{param.Name} \"{EscapeArg(strValue)}\"");
        }

        if (!string.IsNullOrWhiteSpace(skill.ExtraCliArgs))
            parts.Add(skill.ExtraCliArgs);

        return string.Join(' ', parts);
    }

    private static string EscapeArg(string arg) =>
        arg.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string NormalizeToolName(string name) =>
        name.Replace('-', '_').Replace('.', '_').ToLowerInvariant();
}

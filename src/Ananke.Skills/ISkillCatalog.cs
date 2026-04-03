using Ananke.Orchestration.Tools;

namespace Ananke.Skills;

/// <summary>
/// Protocol-agnostic interface for discovering, resolving, and syncing external skills.
/// Implementations bridge specific registries (OpenClaw, future MCP registries, etc.)
/// into Ananke's <see cref="ToolDefinition"/> model.
/// </summary>
public interface ISkillCatalog
{
    /// <summary>
    /// Searches the local catalog cache for skills matching <paramref name="query"/>.
    /// Results are ranked by relevance and local score.
    /// </summary>
    Task<IReadOnlyList<SkillDescriptor>> SearchAsync(
        string query,
        IReadOnlyList<string>? tags = null,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a <see cref="SkillDescriptor"/> into a runnable <see cref="ToolDefinition"/>.
    /// This is the lazy step — the CLI bridge or process wrapper is created here.
    /// </summary>
    Task<ToolDefinition> ResolveAsync(
        SkillDescriptor skill,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches the latest skill metadata from the remote registry and updates the local cache.
    /// Call on startup or on a timer.
    /// </summary>
    Task SyncAsync(CancellationToken ct = default);
}

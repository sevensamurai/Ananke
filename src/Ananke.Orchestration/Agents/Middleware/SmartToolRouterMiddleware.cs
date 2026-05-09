using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Routing;

namespace Ananke.Orchestration.Agents.Middleware;

/// <summary>
/// Middleware that narrows the tool window for each model turn using an
/// <see cref="ISmartToolRouter"/>, and injects inflammation advisories for
/// unhealthy tools.
/// </summary>
/// <remarks>
/// <para>
/// On each call to <see cref="OnBeforeGenerateAsync"/>, the middleware:
/// <list type="number">
///   <item>Extracts the last user message as the routing query.</item>
///   <item>Projects <see cref="ToolKit.Tools"/> to <see cref="ToolMemoryEntry"/> instances.</item>
///   <item>Calls <see cref="ISmartToolRouter.RouteAsync"/> to obtain a <see cref="ToolRoutingDecision"/>.</item>
///   <item>Replaces <see cref="AgentRequest.Tools"/> with the selected subset
///         (or empties it when <see cref="ToolRoutingDecision.UseTools"/> is <see langword="false"/>).</item>
///   <item>When <see cref="ToolKit.Memory"/> is set, appends an inflammation advisory to
///         <see cref="AgentRequest.SystemPrompt"/> for any selected tool in
///         <see cref="ToolHealth.Degraded"/>, <see cref="ToolHealth.Cooldown"/>, or
///         <see cref="ToolHealth.Offline"/> state.</item>
/// </list>
/// </para>
/// <para>
/// When the effective router is <see cref="PassThroughRouter"/> (no router configured on
/// the kit and no explicit router passed) all tools are forwarded unchanged — backward
/// compatible.
/// </para>
/// <code>
/// var model = MiddlewareAgentModel.Wrap(innerModel,
///     new SmartToolRouterMiddleware(kit));
/// </code>
/// </remarks>
public sealed class SmartToolRouterMiddleware : IAgentModelMiddleware
{
    private readonly ToolKit _kit;
    private readonly ISmartToolRouter? _explicitRouter;
    private readonly int _maxSelected;

    /// <summary>Creates the middleware.</summary>
    /// <param name="kit">The tool kit whose tools may be routed.</param>
    /// <param name="router">
    /// Optional explicit router. When <see langword="null"/> the router registered on
    /// <paramref name="kit"/> via <see cref="ToolKit.WithRouter"/> is used; if neither is
    /// set, <see cref="PassThroughRouter.Instance"/> is the effective router.
    /// </param>
    /// <param name="maxSelected">
    /// Soft cap on the number of tools forwarded to the model per turn. Defaults to 8.
    /// </param>
    public SmartToolRouterMiddleware(ToolKit kit, ISmartToolRouter? router = null, int maxSelected = 8)
    {
        ArgumentNullException.ThrowIfNull(kit);
        _kit = kit;
        _explicitRouter = router;
        _maxSelected = maxSelected;
    }

    /// <inheritdoc />
    public async Task<AgentRequest> OnBeforeGenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        if (request.Tools is not { Count: > 0 })
            return request;

        var query = request.Messages
            .LastOrDefault(m => m.Role == AgentRole.User)
            ?.Content;

        if (string.IsNullOrWhiteSpace(query))
            return request;

        var effective = _explicitRouter ?? _kit.Router ?? PassThroughRouter.Instance;

        // Fast path — no allocation when using the pass-through
        if (effective is PassThroughRouter && _kit.Memory is null)
            return request;

        // Project ToolDefinition → ToolMemoryEntry, reading health from kit memory when available
        var candidates = await BuildCandidatesAsync(ct).ConfigureAwait(false);

        var routingRequest = new ToolRoutingRequest
        {
            UserMessage = query,
            Candidates = candidates,
            MaxSelected = _maxSelected,
        };

        var decision = await effective.RouteAsync(routingRequest, ct).ConfigureAwait(false);

        // Resolve selected ToolMemoryEntry back to AgentTool via kit
        IReadOnlyList<AgentTool> selectedTools;
        if (!decision.UseTools)
        {
            selectedTools = [];
        }
        else
        {
            selectedTools = decision.SelectedTools
                .Where(e => _kit.Tools.TryGetValue(e.ToolName, out _))
                .Select(e =>
                {
                    var def = _kit.Tools[e.ToolName];
                    return new AgentTool(def.Name, def.Description, def.ParametersJsonSchema);
                })
                .ToList();
        }

        // Inflammation advisory — inject for unhealthy tools in the selected set
        var systemPrompt = request.SystemPrompt;
        if (_kit.Memory is not null && selectedTools.Count > 0)
        {
            var advisory = await BuildInflammationAdvisoryAsync(decision.SelectedTools, ct)
                .ConfigureAwait(false);
            if (advisory is not null)
                systemPrompt = string.IsNullOrEmpty(systemPrompt)
                    ? advisory
                    : $"{systemPrompt}\n\n{advisory}";
        }

        return request with { Tools = selectedTools, SystemPrompt = systemPrompt };
    }

    /// <inheritdoc />
    public Task<AgentResponse> OnAfterGenerateAsync(
        AgentResponse response, AgentRequest request, CancellationToken ct = default) =>
        Task.FromResult(response);

    // ── Helpers ───────────────────────────────────────────────────────

    private async Task<IReadOnlyList<ToolMemoryEntry>> BuildCandidatesAsync(CancellationToken ct)
    {
        if (_kit.Memory is null)
        {
            return _kit.Tools.Values
                .Select(def => new ToolMemoryEntry
                {
                    ToolName = def.Name,
                    KitName = _kit.Name,
                    Description = def.Description,
                    Tags = def.Tags,
                })
                .ToList();
        }

        // Recall each tool by exact name to get its current health and stats.
        var result = new List<ToolMemoryEntry>(_kit.Tools.Count);
        foreach (var def in _kit.Tools.Values)
        {
            var entries = await _kit.Memory
                .RecallAsync(def.Name, topK: 1, ct: ct)
                .ConfigureAwait(false);

            var entry = entries.FirstOrDefault(e => e.ToolName == def.Name);
            result.Add(entry ?? new ToolMemoryEntry
            {
                ToolName = def.Name,
                KitName = _kit.Name,
                Description = def.Description,
                Tags = def.Tags,
            });
        }
        return result;
    }

    private async Task<string?> BuildInflammationAdvisoryAsync(
        IReadOnlyList<ToolMemoryEntry> selected, CancellationToken ct)
    {
        var lines = new List<string>();

        foreach (var entry in selected)
        {
            // Fetch the current health from memory (entry in the decision may be stale)
            var fresh = await _kit.Memory!.RecallAsync(entry.ToolName, topK: 1, ct: ct)
                .ConfigureAwait(false);
            var current = fresh.FirstOrDefault(e => e.ToolName == entry.ToolName);

            if (current is null)
            {
                if (_kit.Tools.ContainsKey(entry.ToolName))
                    lines.Add($"NOTE: `{entry.ToolName}` is currently offline — do not call it.");
            }
            else if (current.Health is ToolHealth.Degraded)
            {
                lines.Add($"NOTE: `{entry.ToolName}` is currently degraded — it may fail; prefer an alternative if available.");
            }
            else if (current.Health is ToolHealth.Cooldown)
            {
                lines.Add($"NOTE: `{entry.ToolName}` is in cooldown after recent failures — do not call it this turn.");
            }
        }

        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }
}

using Ananke.Abstractions.Tools;

namespace Ananke.Orchestration.Tools.Faults;

/// <summary>
/// Background service that removes idle tools from <see cref="IToolMemory"/> when they
/// have not been recalled within a configurable TTL or have not accumulated enough hits.
/// </summary>
/// <remarks>
/// <para>
/// Pruning rules (evaluated every <see cref="CheckInterval"/>):
/// <list type="bullet">
///   <item>A tool is pruned when <c>LastUsed</c> is older than <see cref="IdleTtl"/> AND
///     <c>HitCount</c> is below <see cref="MinHitCount"/>.</item>
///   <item><see cref="ToolHealth.Offline"/> tools are always eligible for pruning.</item>
///   <item>Pinned tools (<see cref="ToolKit.PinnedToolNames"/>) are never pruned.</item>
/// </list>
/// </para>
/// <code>
/// var pruner = new ToolPruner(memory, kit);
/// await pruner.StartAsync();
/// </code>
/// </remarks>
public sealed class ToolPruner : IAsyncDisposable
{
    private readonly IToolMemory _memory;
    private readonly ToolKit _kit;
    private readonly TimeSpan _checkInterval;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>
    /// How long a tool can go without being recalled before it becomes eligible for pruning.
    /// Defaults to 24 hours.
    /// </summary>
    public TimeSpan IdleTtl { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Minimum lifetime recall count below which a tool is eligible for pruning.
    /// A tool must satisfy BOTH the TTL and hit-count thresholds to be pruned.
    /// Defaults to <c>3</c>.
    /// </summary>
    public int MinHitCount { get; init; } = 3;

    /// <summary>How often the pruning loop runs. Defaults to 1 hour.</summary>
    public TimeSpan CheckInterval => _checkInterval;

    /// <summary>
    /// Creates a pruner.
    /// </summary>
    /// <param name="memory">The tool memory to remove pruned entries from.</param>
    /// <param name="kit">The kit whose tools are tracked (pinned tools are exempt).</param>
    /// <param name="checkInterval">How often the background loop runs. Defaults to 1 h.</param>
    public ToolPruner(
        IToolMemory memory,
        ToolKit kit,
        TimeSpan? checkInterval = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(kit);
        _memory = memory;
        _kit = kit;
        _checkInterval = checkInterval ?? TimeSpan.FromHours(1);
    }

    /// <summary>Starts the background pruning loop.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>Stops the background pruning loop.</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try { await (_loopTask ?? Task.CompletedTask).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }

    // ── Pruning logic ─────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, ct).ConfigureAwait(false);
                await TickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Runs a single pruning evaluation cycle.
    /// Public for testing — the production path calls this from the background loop.
    /// </summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var pinned = _kit.PinnedToolNames;

        foreach (var toolName in _kit.Tools.Keys)
        {
            if (pinned.Contains(toolName))
                continue;

            var entries = await _memory.RecallAsync(toolName, topK: 1, ct: ct)
                .ConfigureAwait(false);
            var entry = entries.FirstOrDefault(e => e.ToolName == toolName);

            if (ShouldPrune(entry, now))
            {
                await _memory.RemoveAsync(_kit.Name, toolName, ct).ConfigureAwait(false);
                ToolMetrics.Pruned.Add(1,
                    new KeyValuePair<string, object?>("kit", _kit.Name),
                    new KeyValuePair<string, object?>("tool", toolName));
            }
        }
    }

    private bool ShouldPrune(ToolMemoryEntry? entry, DateTimeOffset now)
    {
        if (entry is null)
            return false; // Not in memory at all — already pruned or never upserted

        if (entry.Health == ToolHealth.Offline)
            return true;

        var idleEnough = entry.LastUsed == DateTimeOffset.MinValue
            || now - entry.LastUsed >= IdleTtl;

        var lowHits = entry.HitCount < MinHitCount;

        return idleEnough && lowHits;
    }
}

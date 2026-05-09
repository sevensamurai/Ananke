using Ananke.Abstractions.Tools;

namespace Ananke.Orchestration.Tools.Faults;

/// <summary>
/// Background timer that automatically restores <see cref="ToolHealth"/> states after their
/// configured recovery windows have elapsed.
/// </summary>
/// <remarks>
/// <para>
/// Recovery rules (checked every <see cref="CheckInterval"/>):
/// <list type="bullet">
///   <item><see cref="ToolHealth.Cooldown"/> → <see cref="ToolHealth.Healthy"/>
///     after <see cref="CooldownDuration"/> has elapsed since the fault was reported.</item>
///   <item><see cref="ToolHealth.Degraded"/> → <see cref="ToolHealth.Healthy"/>
///     after <see cref="DegradedDuration"/> has elapsed.</item>
///   <item><see cref="ToolHealth.Offline"/> is permanent for the lifetime of this instance
///     (represents a contract break — only explicit re-registration resets it).</item>
/// </list>
/// </para>
/// <para>
/// Call <see cref="StartAsync"/> to begin the background loop and
/// <see cref="StopAsync"/> (or dispose) to stop it.
/// The recovery loop is lightweight — it only touches entries registered via
/// <see cref="TrackRecovery"/> whose window has elapsed.
/// </para>
/// <para>
/// Corresponds to the inflammation-resolution (health decay) role in the semantic tool gate.
/// </para>
/// </remarks>
public sealed class ToolHealthRecovery : IAsyncDisposable
{
    private readonly IToolMemory _memory;
    private readonly TimeSpan _checkInterval;
    private readonly TimeProvider _timeProvider;

    private readonly Dictionary<(string Kit, string Tool), (ToolHealth Health, DateTimeOffset Since)> _tracked = [];
    private readonly Lock _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>How long a tool stays in <see cref="ToolHealth.Cooldown"/> before recovering.</summary>
    public TimeSpan CooldownDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a tool stays in <see cref="ToolHealth.Degraded"/> before recovering.</summary>
    public TimeSpan DegradedDuration { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the decay loop runs. Defaults to 30 seconds.</summary>
    public TimeSpan CheckInterval => _checkInterval;

    /// <summary>
    /// Creates a health recovery manager.
    /// </summary>
    /// <param name="memory">The tool memory to write recovered health states into.</param>
    /// <param name="checkInterval">How often to scan for expired windows. Defaults to 30 s.</param>
    /// <param name="timeProvider">Time abstraction for testing. Defaults to <see cref="TimeProvider.System"/>.</param>
    public ToolHealthRecovery(IToolMemory memory, TimeSpan? checkInterval = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Registers a tool for timed health recovery after a fault was reported.
    /// A no-op for <see cref="ToolHealth.Healthy"/> (no recovery needed) and
    /// <see cref="ToolHealth.Offline"/> (permanent — not time-bounded).
    /// </summary>
    public void TrackRecovery(string kitName, string toolName, ToolHealth health)
    {
        if (health is ToolHealth.Healthy or ToolHealth.Offline)
            return; // Healthy needs no recovery; Offline is permanent

        lock (_lock)
            _tracked[(kitName, toolName)] = (health, _timeProvider.GetUtcNow());
    }

    /// <summary>Starts the background decay loop.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>Stops the background decay loop and waits for it to finish.</summary>
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

    // ── Internal ─────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Delay(_timeProvider, _checkInterval, ct).ConfigureAwait(false);
                await TickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task Delay(TimeProvider timeProvider, TimeSpan delay, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), tcs);
        using var timer = timeProvider.CreateTimer(
            static s => ((TaskCompletionSource)s!).TrySetResult(),
            tcs, delay, Timeout.InfiniteTimeSpan);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        List<(string Kit, string Tool)>? recovered = null;

        lock (_lock)
        {
            foreach (var (key, (health, since)) in _tracked)
            {
                var duration = health == ToolHealth.Cooldown ? CooldownDuration : DegradedDuration;
                if (now - since >= duration)
                    (recovered ??= []).Add(key);
            }

            if (recovered is not null)
                foreach (var key in recovered)
                    _tracked.Remove(key);
        }

        if (recovered is null) return;

        foreach (var (kit, tool) in recovered)
            await _memory.MarkHealthAsync(kit, tool, ToolHealth.Healthy, ct).ConfigureAwait(false);
    }
}

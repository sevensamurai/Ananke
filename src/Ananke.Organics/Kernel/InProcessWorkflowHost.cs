using System.Collections.Concurrent;

namespace Ananke.Organics.Kernel;

/// <summary>
/// In-process <see cref="IWorkflowHost"/> for development, demos, and tests.
/// Each cell runs as a <see cref="Task"/> with a dedicated
/// <see cref="CancellationTokenSource"/>.
/// </summary>
public sealed class InProcessWorkflowHost : IWorkflowHost
{
    private readonly ConcurrentDictionary<string, CellEntry> _cells = new();

    /// <inheritdoc />
    public Task StartAsync(string name, Func<CancellationToken, Task> workflowLoop, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workflowLoop);

        var cts = new CancellationTokenSource();
        var entry = new CellEntry(cts, workflowLoop);
        var task = Task.Run(() => RunLoop(name, workflowLoop, cts.Token, entry), CancellationToken.None);
        entry.SetLoop(task);

        if (!_cells.TryAdd(name, entry))
        {
            cts.Dispose();
            throw new InvalidOperationException($"A cell named '{name}' is already alive.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_cells.TryRemove(name, out var entry))
            return;

        await CancelAndDispose(entry).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListActive() =>
        _cells.Keys.ToList();

    /// <summary>
    /// Pauses a running cell by cancelling its current loop. The cell
    /// remains in <see cref="ListActive"/> but stops executing. Call
    /// <see cref="ResumeAsync"/> to restart it with a fresh token.
    /// </summary>
    public async Task PauseAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_cells.TryGetValue(name, out var entry) || entry.Paused)
            return;

        // Mark as paused BEFORE cancelling — RunLoop's finally checks this flag
        var pausedEntry = entry with { Paused = true };
        if (!_cells.TryUpdate(name, pausedEntry, entry))
            return;

        // Cancel the current loop and wait for it to fully unwind
        await entry.Cts.CancelAsync().ConfigureAwait(false);
        try { await entry.Loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception) { /* swallow crash during pause */ }
        entry.Cts.Dispose();

        // Signal that the pause has fully taken effect
        pausedEntry.PausedTcs.TrySetResult();
    }

    /// <summary>
    /// Resumes a previously paused cell. Restarts the loop with a fresh
    /// cancellation token. No-op if the cell is not paused.
    /// </summary>
    public Task ResumeAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_cells.TryGetValue(name, out var entry) || !entry.Paused)
            return Task.CompletedTask;

        var cts = new CancellationTokenSource();
        var loop = entry.WorkflowLoop;
        var newEntry = new CellEntry(cts, loop);
        var task = Task.Run(() => RunLoop(name, loop, cts.Token, newEntry), CancellationToken.None);
        newEntry.SetLoop(task);

        _cells.TryUpdate(name, newEntry, entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var entries = _cells.ToArray();
        _cells.Clear();

        foreach (var (_, entry) in entries)
        {
            if (!entry.Paused)
                await CancelAndDispose(entry).ConfigureAwait(false);
            else
                entry.Cts.Dispose();
        }
    }

    // ── Internal observation hooks ──────────────────────────────────────────
    // Exposed only to Ananke.Organics.Tests via InternalsVisibleTo.
    // Each returns a Task that completes when the named lifecycle event fires.
    // Always pair with WaitAsync(TimeSpan) to avoid hangs on regressions.

    /// <summary>
    /// Returns a <see cref="Task"/> that completes when the cell's loop delegate
    /// has been invoked for the first time (i.e. the cell is running).
    /// </summary>
    internal Task WhenStartedAsync(string name) =>
        _cells.TryGetValue(name, out var e) ? e.StartedTcs.Task : Task.CompletedTask;

    /// <summary>
    /// Returns a <see cref="Task"/> that completes when a <see cref="PauseAsync"/>
    /// call has fully unwound the in-flight loop iteration.
    /// </summary>
    internal Task WhenPausedAsync(string name) =>
        _cells.TryGetValue(name, out var e) ? e.PausedTcs.Task : Task.CompletedTask;

    /// <summary>
    /// Returns a <see cref="Task"/> that completes when the cell's loop has exited
    /// (either stopped, crashed, or disposed).
    /// </summary>
    internal Task WhenStoppedAsync(string name) =>
        _cells.TryGetValue(name, out var e) ? e.StoppedTcs.Task : Task.CompletedTask;

    private async Task RunLoop(string name, Func<CancellationToken, Task> loop, CancellationToken ct, CellEntry entry)
    {
        try
        {
            entry.StartedTcs.TrySetResult();
            await loop(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected — cell was killed or paused.
        }
        catch (Exception)
        {
            // Cell crashed — remove from alive list so it's not sensed.
        }
        finally
        {
            // Only remove if not paused (paused cells stay in _cells)
            if (_cells.TryGetValue(name, out var current) && !current.Paused)
                _cells.TryRemove(name, out _);

            entry.StoppedTcs.TrySetResult();
        }
    }

    private static async Task CancelAndDispose(CellEntry entry)
    {
        try
        {
            await entry.Cts.CancelAsync().ConfigureAwait(false);
            await entry.Loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
        catch
        {
            // Cell threw during shutdown — swallow.
        }
        finally
        {
            entry.Cts.Dispose();
        }
    }

    private sealed record CellEntry(
        CancellationTokenSource Cts,
        Func<CancellationToken, Task> WorkflowLoop,
        bool Paused = false)
    {
        // Loop task is set immediately after construction, before the entry is
        // visible to other threads (TryAdd acts as the publish barrier).
        private Task _loop = Task.CompletedTask;
        internal Task Loop => _loop;
        internal void SetLoop(Task t) => _loop = t;

        internal readonly TaskCompletionSource StartedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource PausedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource StoppedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

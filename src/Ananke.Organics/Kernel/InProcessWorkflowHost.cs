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

    // Held while a cell is added and while DisposeAsync marks the host disposed, so every cell
    // is either added before DisposeAsync reads _cells or refused.
    private readonly Lock _startGate = new();
    private bool _disposed;

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The host has been disposed, or is being disposed.</exception>
    public Task StartAsync(string name, Func<CancellationToken, Task> workflowLoop, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workflowLoop);

        var cts = new CancellationTokenSource();
        var entry = new CellEntry(cts, workflowLoop);
        var run = CreateLoop(name, entry);

        lock (_startGate)
        {
            if (_disposed || !_cells.TryAdd(name, entry))
            {
                cts.Dispose();
                ObjectDisposedException.ThrowIf(_disposed, this);
                throw new InvalidOperationException($"A cell named '{name}' is already alive.");
            }
        }

        run.Start(TaskScheduler.Default);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_cells.TryRemove(name, out var entry))
            return;

        await StopEntry(entry, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListActive() =>
        _cells.Keys.ToList();

    /// <summary>
    /// Pauses a running cell by cancelling its current loop. The cell
    /// remains in <see cref="ListActive"/> but stops executing. Call
    /// <see cref="ResumeAsync"/> to restart it with a fresh token.
    /// A loop that ends on its own, by returning before its token is cancelled or by throwing
    /// other than by its cancellation, leaves the cell instead, as it would if not paused.
    /// If <paramref name="ct"/> fires before the loop has unwound, the cell stays paused and
    /// the pause settles once the loop ends.
    /// </summary>
    public async Task PauseAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_cells.TryGetValue(name, out var entry) || entry.Paused)
            return;

        // Mark as paused BEFORE cancelling — RunLoop's finally checks this flag
        var pausedEntry = entry with { Paused = true };
        if (!_cells.TryUpdate(name, pausedEntry, entry))
            return;

        // The pause settles when the loop has unwound, whether or not the caller still waits:
        // the token source is disposed then, and PausedTcs completed.
        try { await entry.Cts.CancelAsync().ConfigureAwait(false); }
        finally { _ = SettlePause(entry); }
        await entry.PausedTcs.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes a previously paused cell. Restarts the loop with a fresh
    /// cancellation token, once the pause has settled. No-op if the cell is not paused.
    /// </summary>
    public async Task ResumeAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_cells.TryGetValue(name, out var entry) || !entry.Paused)
            return;

        // A pause still unwinding (or one whose caller gave up waiting) leaves the old loop
        // running: starting the new one now would run two loops of the same cell at once.
        await entry.PausedTcs.Task.WaitAsync(ct).ConfigureAwait(false);

        var cts = new CancellationTokenSource();
        var newEntry = new CellEntry(cts, entry.WorkflowLoop);
        var run = CreateLoop(name, newEntry);

        if (!_cells.TryUpdate(name, newEntry, entry))
        {
            cts.Dispose();
            return;
        }

        run.Start(TaskScheduler.Default);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_startGate)
            _disposed = true;

        foreach (var name in _cells.Keys)
        {
            if (_cells.TryRemove(name, out var entry))
                await StopEntry(entry).ConfigureAwait(false);
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

    // The loop is created unstarted and started only once its entry is in _cells: a loop that
    // ends at once would otherwise run RunLoop's removal before the entry is added, and the
    // ended cell would stay in ListActive. The token is read here, while the source is not yet
    // shared: a stop or pause before the loop starts may dispose it.
    private Task<Task> CreateLoop(string name, CellEntry entry)
    {
        var token = entry.Cts.Token;
        var run = new Task<Task>(
            () => RunLoop(name, entry.WorkflowLoop, token, entry), TaskCreationOptions.DenyChildAttach);
        entry.SetLoop(run.Unwrap());
        return run;
    }

    private async Task RunLoop(string name, Func<CancellationToken, Task> loop, CancellationToken ct, CellEntry entry)
    {
        var ended = false;
        try
        {
            entry.StartedTcs.TrySetResult();
            await loop(ct).ConfigureAwait(false);
            ended = !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected — cell was killed or paused.
        }
        catch (Exception)
        {
            // Cell crashed — remove from alive list so it's not sensed.
            ended = true;
        }
        finally
        {
            // Removes only this loop's own entry, by record equality: a cell started or resumed
            // under the same name since differs. Its paused copy (paused cells stay in _cells)
            // differs too, and is removed only when the loop ended on its own: it crashed, or
            // returned before its token was cancelled.
            if (!_cells.TryRemove(KeyValuePair.Create(name, entry)) && ended)
                _cells.TryRemove(KeyValuePair.Create(name, entry with { Paused = true }));

            entry.StoppedTcs.TrySetResult();
        }
    }

    // RunLoop catches every exception of the loop, so Loop never faults.
    private static async Task SettlePause(CellEntry entry)
    {
        await entry.Loop.ConfigureAwait(false);
        entry.Cts.Dispose();
        entry.PausedTcs.TrySetResult();
    }

    // A paused cell's loop is already cancelled, and its token source is disposed by the pause.
    private static Task StopEntry(CellEntry entry, CancellationToken ct = default) =>
        entry.Paused ? entry.PausedTcs.Task.WaitAsync(ct) : CancelAndDispose(entry, ct);

    private static async Task CancelAndDispose(CellEntry entry, CancellationToken ct)
    {
        try
        {
            await entry.Cts.CancelAsync().ConfigureAwait(false);
            await entry.Loop.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (AggregateException)
        {
            // A callback registered on the loop's token threw.
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
        // visible to other threads (TryAdd / TryUpdate act as the publish barrier).
        private Task _loop = Task.CompletedTask;
        internal Task Loop => _loop;
        internal void SetLoop(Task t) => _loop = t;

        internal readonly TaskCompletionSource StartedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource PausedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource StoppedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

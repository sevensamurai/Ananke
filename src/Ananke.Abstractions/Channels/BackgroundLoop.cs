using System.Threading.Channels;

namespace Ananke.Abstractions.Channels;

/// <summary>
/// The bounded queue and the processing loop behind <see cref="BackgroundProcessor{T}"/> and
/// <see cref="BackgroundProcessor{T, A}"/>.
/// </summary>
internal sealed class BackgroundLoop<TItem>
{
    private readonly Type _owner;
    private readonly Func<TItem, CancellationToken, Task> _handle;
    private readonly Channel<TItem> _queue;
    private readonly Action<Exception, TItem>? _onError;
    private readonly Action<string>? _onInfo;
    private readonly Lock _gate = new();
    // True on the loop and in every task it starts: there, waiting for the loop would wait for itself.
    private readonly AsyncLocal<bool> _onLoop = new();
    private CancellationTokenSource? _cts;
    // True when the loop ended because the queue was completed and empty; false when it was cancelled.
    private Task<bool> _processingTask = Task.FromResult(true);
    // Set by the first DisposeAsync; every later call awaits the same stop.
    private Task? _disposal;

    public BackgroundLoop(
        Type owner,
        Func<TItem, CancellationToken, Task> handle,
        int capacity,
        Action<Exception, TItem>? onError,
        Action<string>? onInfo)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _owner = owner;
        _handle = handle;
        _onError = onError;
        _onInfo = onInfo;
        _queue = Channel.CreateBounded<TItem>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _cts is not null && !_processingTask.IsCompleted;
        }
    }

    public void Start(CancellationToken ct)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, _owner);
            StartLocked(ct);
        }
    }

    public ValueTask EnqueueAsync(TItem item, CancellationToken ct) => _queue.Writer.WriteAsync(item, ct);

    public bool TryEnqueue(TItem item) => _queue.Writer.TryWrite(item);

    // Throws at the call, not in the task, so a call nobody awaits does not fail without a word.
    public Task CompleteAsync(CancellationToken ct)
    {
        ThrowIfOnLoop(nameof(CompleteAsync));
        return CompleteCoreAsync(ct);
    }

    private async Task CompleteCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Task<bool> loop;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, _owner);
            _queue.Writer.TryComplete();
            StartLocked(CancellationToken.None);
            loop = _processingTask;
        }

        if (!await loop.WaitAsync(ct).ConfigureAwait(false))
            throw new OperationCanceledException("The processor was stopped before every queued item was handled.");
    }

    public ValueTask DisposeAsync()
    {
        ThrowIfOnLoop(nameof(DisposeAsync));
        lock (_gate)
        {
            _disposal ??= StopAsync(_cts, _processingTask);
            return new ValueTask(_disposal);
        }
    }

    // Runs under _gate until its first await: completing the writer and requesting cancellation
    // only schedule continuations, they never run one inline.
    private async Task StopAsync(CancellationTokenSource? cts, Task<bool> loop)
    {
        _queue.Writer.TryComplete();
        if (cts is null) return;

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
            await loop.ConfigureAwait(false);
        }
        finally
        {
            cts.Dispose();
        }
    }

    // Only while the loop runs: a task the worker started may outlive the loop, and then there is
    // nothing left to wait for.
    private void ThrowIfOnLoop(string method)
    {
        if (!_onLoop.Value) return;
        lock (_gate)
        {
            if (_processingTask.IsCompleted) return;
        }
        throw new InvalidOperationException(
            $"{method} was called from the worker of {_owner.Name}; it waits for the loop the worker runs on.");
    }

    private void StartLocked(CancellationToken ct)
    {
        if (_cts is not null) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        _processingTask = Task.Run(() => ProcessAsync(token), CancellationToken.None);
    }

    // Never throws: a worker or callback exception is reported, and cancellation ends the loop.
    private async Task<bool> ProcessAsync(CancellationToken ct)
    {
        _onLoop.Value = true;
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await _handle(item, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ReportError(ex, item);
                }
            }
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ReportInfo("Processing stopped (cancellation requested)");
            return false;
        }
    }

    private void ReportError(Exception ex, TItem item)
    {
        if (_onError is null) return;
        try
        {
            _onError(ex, item);
        }
        catch (Exception callbackEx)
        {
            ReportInfo($"onError threw while reporting {ex.GetType().Name}: {callbackEx}");
        }
    }

    private void ReportInfo(string message)
    {
        if (_onInfo is null) return;
        try
        {
            _onInfo(message);
        }
        catch (Exception)
        {
            // onInfo is the last place a failure can be reported; the loop goes on without it.
        }
    }
}

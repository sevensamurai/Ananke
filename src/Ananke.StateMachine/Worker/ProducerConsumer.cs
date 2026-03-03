using Ananke.Abstractions.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;

namespace Ananke.StateMachine.Worker;

/// <summary>
/// Producer consumer queue
/// reference https://michaelscodingspot.com/performance-of-producer-consumer/
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="worker"></param>
/// <param name="logger"></param>
public class ProducerConsumer<T>(IBackgroundWorker<T>? worker, ILogger<ProducerConsumer<T>>? logger = null)
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
    private readonly ILogger<ProducerConsumer<T>> _logger = logger ?? NullLogger<ProducerConsumer<T>>.Instance;

    private Task _backgroundWorker = Task.CompletedTask;

    public bool IsRunning { get; private set; }

    private async Task ProcessQueueAsync(CancellationToken token)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(token))
            {
                while (_channel.Reader.TryRead(out T? item))
                {
                    if (worker is null) continue;

                    try
                    {
                        await worker.HandleAsync(item, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing item of type {ItemType}: {Message}",
                            typeof(T).Name, ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Processing queue stopped (cancellation requested)");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Processing queue terminated unexpectedly: {Message}", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void Start(CancellationToken token)
    {
        if (!IsRunning)
        {
            IsRunning = true;
            var process = Task.Factory.StartNew(async () => await ProcessQueueAsync(token),
                TaskCreationOptions.LongRunning);
            _backgroundWorker = process.Unwrap();
        }
    }

    public void Stop()
    {
        if (IsRunning)
        {
            IsRunning = false;
            _channel.Writer.TryComplete();
            _backgroundWorker?.Wait();
        }
    }

    public void Queue(T item)
    {
        _channel.Writer.TryWrite(item);
    }

    public async Task QueueAsync(T item)
    {
        await _channel.Writer.WriteAsync(item);
    }

    public void MarkAsCompleted()
    {
        _channel.Writer.Complete();
    }

    public Task IsDone() => _channel.Reader.Completion;

    public Task WhenAll() => _backgroundWorker;
}

using Ananke.Abstractions.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Memory;

/// <summary>
/// Periodically calls <see cref="IConversationMemory.CleanupExpiredAsync"/> to remove
/// sessions whose TTL has elapsed. Registered as a singleton via
/// <see cref="Extensions.OrchestrationOptions.UseMemoryCleanup"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="System.Threading.Timer"/> internally and implements <see cref="IDisposable"/>
/// so the DI container stops the timer at shutdown.
/// </remarks>
internal sealed class ConversationMemoryCleanupTimer : IDisposable
{
    private readonly IConversationMemory _memory;
    private readonly ILogger _logger;
    private readonly Timer _timer;

    public ConversationMemoryCleanupTimer(
        IConversationMemory memory,
        TimeSpan interval,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        _memory = memory;
        _logger = loggerFactory?.CreateLogger<ConversationMemoryCleanupTimer>()
            ?? NullLogger<ConversationMemoryCleanupTimer>.Instance;
        _timer = new Timer(OnTick, null, interval, interval);
    }

    private async void OnTick(object? state)
    {
        try
        {
            await _memory.CleanupExpiredAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversation memory cleanup failed");
        }
    }

    public void Dispose() => _timer.Dispose();
}
